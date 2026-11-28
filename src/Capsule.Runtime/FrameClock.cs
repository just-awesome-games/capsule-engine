using System.Diagnostics;

namespace Capsule.Runtime;

// One host frame's sample, in wall-clock milliseconds from its start to the next frame's, and the fixed steps
// it ran.
internal readonly record struct FrameSample(double IntervalMs, double UpdateMs, double DrawMs, int Steps);

// Times each host frame's update, which frame diagnostics write and the overlay's frame pane shows. The host
// reads the clock here twice a frame, and only a development build makes one.
internal sealed class FrameClock(Func<long> timestamp)
{
    // When the frame in progress began, and when its update ended.
    internal long Start { get; private set; }

    internal long UpdateEnd { get; private set; }

    internal int Steps { get; private set; }

    // The frames begun so far.
    internal long Frames { get; private set; }

    // The frame the latest BeginUpdate closed, zero until a second frame begins.
    internal FrameSample Last { get; private set; }

    // lastDrawMs is the cost of the last frame's draw, which the host measured.
    internal void BeginUpdate(double lastDrawMs)
    {
        long now = timestamp();
        if (Frames > 0)
        {
            Last = new FrameSample(Milliseconds(now - Start), Milliseconds(UpdateEnd - Start), lastDrawMs, Steps);
        }

        Start = now;
        Frames++;
    }

    // steps: The fixed steps the update ran, zero on a frame that had not accumulated one.
    internal void EndUpdate(int steps)
    {
        UpdateEnd = timestamp();
        Steps = steps;
    }

    internal static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
