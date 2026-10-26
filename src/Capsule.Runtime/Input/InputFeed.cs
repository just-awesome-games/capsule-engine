using Capsule.Runtime.Sdl;

namespace Capsule.Runtime.Input;

// Records InputEvents from the SDL events the host's event callback hands it, on the main thread
// between frames. One instance per windowed host.
internal sealed unsafe class InputFeed
{
    private readonly TriggerEdges _triggers = new();
    private float _triggerDeadzone = float.NaN;

    // Two buffers swapped at each take. A take allocates nothing.
    private InputEvents _recording;
    private InputEvents _taken;

    internal InputFeed()
    {
        _recording = new InputEvents(_triggers);
        _taken = new InputEvents(_triggers);
    }

    // Makes a trigger pulled past deadzone a press from the next event on. No trigger motion is a
    // press until the first call, and a repeated deadzone costs nothing.
    internal void UseTriggerDeadzone(float deadzone)
    {
        if (deadzone != _triggerDeadzone)
        {
            _triggerDeadzone = deadzone;
            _triggers.Threshold = GamepadSampler.PressThreshold(deadzone);
        }
    }

    internal void Record(nint sdlEvent) => _recording.Record((byte*)sdlEvent);

    // Everything recorded since the last take, which then starts a new record. Valid until the next take.
    internal InputEvents Take()
    {
        (_recording, _taken) = (_taken, _recording);
        _recording.Clear();

        return _taken;
    }
}
