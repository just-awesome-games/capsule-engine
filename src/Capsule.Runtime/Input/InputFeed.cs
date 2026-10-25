using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Runtime.Sdl;

namespace Capsule.Runtime.Input;

// Records InputEvents through an SDL event watch. The backend pumps the events. SDL calls the watch
// from whichever thread pushed one, and the record is locked. One instance per windowed host.
// SDL's events subsystem must be up for its lifetime.
internal sealed unsafe class InputFeed : IDisposable
{
    private readonly Lock _gate = new();
    private readonly GCHandle _self;

    private readonly TriggerEdges _triggers = new();
    private float _triggerDeadzone = float.NaN;

    // Two buffers swapped at each take. A take allocates nothing, and the watch never waits on a
    // sample's reads.
    private InputEvents _recording;
    private InputEvents _taken;

    // The message of an exception the watch caught, reported by the next take.
    private string? _fault;
    private bool _disposed;

    internal InputFeed()
    {
        _recording = new InputEvents(_triggers);
        _taken = new InputEvents(_triggers);
        _self = GCHandle.Alloc(this);
        if (!Sdl3.SDL_AddEventWatch(&OnEvent, GCHandle.ToIntPtr(_self)))
        {
            string error = Sdl3.Error();
            _self.Free();
            throw new InvalidOperationException($"The input event watch could not be installed: {error}. Initialise SDL's events subsystem before constructing the feed.");
        }
    }

    // Makes a trigger pulled past deadzone a press from the next pump on. No trigger motion is a press
    // until the first call, and a repeated deadzone costs nothing.
    internal void UseTriggerDeadzone(float deadzone)
    {
        if (deadzone != _triggerDeadzone)
        {
            _triggerDeadzone = deadzone;
            _triggers.Threshold = GamepadSampler.PressThreshold(deadzone);
        }
    }

    // Everything recorded since the last take, which then starts a new record. Valid until the next take.
    internal InputEvents Take()
    {
        string? fault;
        lock (_gate)
        {
            (_recording, _taken) = (_taken, _recording);
            _recording.Clear();
            fault = _fault;
            _fault = null;
        }

        if (fault is not null)
        {
            Log.Warning($"An input event was not recorded: {fault}");
        }

        return _taken;
    }

    // SDL holds its watch list's lock while it calls a watch, and removing one waits on that lock. No
    // call is in flight once the removal returns, and the handle can go.
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Sdl3.SDL_RemoveEventWatch(&OnEvent, GCHandle.ToIntPtr(_self));
        _self.Free();
    }

    // The return value is ignored for a watch. An exception unwinding into SDL's stack terminates the
    // process, and a lost event costs one press.
    [UnmanagedCallersOnly]
    private static byte OnEvent(nint userData, nint sdlEvent)
    {
        if (GCHandle.FromIntPtr(userData).Target is not InputFeed feed || sdlEvent == nint.Zero)
        {
            return 1;
        }

        try
        {
            lock (feed._gate)
            {
                feed._recording.Record((byte*)sdlEvent);
            }
        }
        catch (Exception error)
        {
            Interlocked.CompareExchange(ref feed._fault, error.Message, null);
        }

        return 1;
    }
}
