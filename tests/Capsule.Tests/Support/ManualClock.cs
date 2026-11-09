using System.Diagnostics;

namespace Capsule.Tests;

// A Stopwatch timestamp that moves when a spec advances it, and by perRead on every read.
internal sealed class ManualClock(long start = 0, long perRead = 0)
{
    internal long Timestamp { get; private set; } = start;

    internal int Reads { get; private set; }

    internal long Read()
    {
        Reads++;

        return Timestamp += perRead;
    }

    internal void Advance(double seconds) => Timestamp += (long)(seconds * Stopwatch.Frequency);
}
