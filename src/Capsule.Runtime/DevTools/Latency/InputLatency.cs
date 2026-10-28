using System.Globalization;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.DevTools.Latency;

// Measures each press from its event's SDL timestamp to the return of the present of the first frame
// whose steps consumed it. A press taken on a frame that runs no step waits for the next frame that
// does. Every press's own latency is kept in a buffer sized at construction, and the file is written
// once, when the host is disposed. Development-only: the host builds one only when a run asks for it.
internal sealed class InputLatency(string path) : IDisposable
{
    // About 18 minutes of a press every 16 ms. Presses past it go unmeasured.
    private const int Capacity = 65536;

    // A frame takes at most InputEvents.MaxTimedPresses, and a run of stepless frames adds up.
    private const int Waiting = 1024;

    private readonly ulong[] _pressed = new ulong[Capacity];
    private readonly ulong[] _presented = new ulong[Capacity];
    private readonly ulong[] _waiting = new ulong[Waiting];
    private int _waitingCount;
    private int _consumedCount;
    private int _count;

    // The presses a focused frame took from the feed, waiting for a step.
    internal void Take(ReadOnlySpan<ulong> presses)
    {
        int room = Math.Min(presses.Length, Waiting - _waitingCount);
        presses[..room].CopyTo(_waiting.AsSpan(_waitingCount));
        _waitingCount += room;
    }

    // After the frame's steps. A frame that ran one consumed every press waiting.
    internal void Stepped(int steps)
    {
        if (steps > 0)
        {
            _consumedCount = _waitingCount;
        }
    }

    // After the frame's present returns. The consumed presses are measured, and the rest wait on.
    internal void Presented()
    {
        if (_consumedCount == 0)
        {
            return;
        }

        ulong now = Sdl3.SDL_GetTicksNS();
        for (int index = 0; index < _consumedCount && _count < Capacity; index++, _count++)
        {
            _pressed[_count] = _waiting[index];
            _presented[_count] = now;
        }

        _waiting.AsSpan(_consumedCount, _waitingCount - _consumedCount).CopyTo(_waiting);
        _waitingCount -= _consumedCount;
        _consumedCount = 0;
    }

    // One row a measured press, both times in SDL_GetTicksNS nanoseconds.
    public void Dispose()
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        using StreamWriter writer = new(path, append: false);
        writer.WriteLine("pressedNs,presentedNs");
        for (int index = 0; index < _count; index++)
        {
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{_pressed[index]},{_presented[index]}"));
        }
    }
}
