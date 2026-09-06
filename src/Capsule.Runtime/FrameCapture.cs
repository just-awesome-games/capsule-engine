using System.Globalization;

namespace Capsule.Runtime;

// The ticks a run saves a picture of, in ascending order with a cursor over them. Owned by the
// host and reached only through WithFrameCapture; the frame path pays one null check without it.
internal sealed class FrameCapture
{
    private readonly string _directory;
    private readonly long[] _ticks;

    private int _next;

    // directory: Created here, at run start, so the first capture cannot fail on a missing folder.
    //
    // ticks: The simulation ticks to capture, in any order and with repeats; each captures once.
    internal FrameCapture(string directory, long[] ticks)
    {
        _directory = directory;

        long[] ordered = [.. ticks];
        Array.Sort(ordered);
        _ticks = [.. ordered.Distinct()];

        Directory.CreateDirectory(directory);
    }

    // The path of the next capture due once completedTick has been simulated, or false when none
    // is. Called until it says no: one frame may pass several listed ticks, and each owes a file.
    internal bool TryTakeDue(long completedTick, out string path)
    {
        if (_next >= _ticks.Length || completedTick < _ticks[_next])
        {
            path = "";
            return false;
        }

        path = Path.Combine(
            _directory,
            string.Create(CultureInfo.InvariantCulture, $"frame-{_ticks[_next++]}.png"));

        return true;
    }
}
