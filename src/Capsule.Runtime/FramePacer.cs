using System.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime;

// Holds the window's frames to the run's frame cap by sleeping until each is due. Due times fall one
// period apart. A late wake shortens the next wait, and the rate holds. A frame more than a period
// late starts the schedule again from itself instead of bursting to catch up. It sleeps on the
// operating system's high-resolution timer and never spins, and the processor idles between frames.
// Times are Stopwatch ticks.
internal sealed class FramePacer(Func<long> timestamp, Action<long> sleep)
{
    private long _period;
    private long _due;

    internal FramePacer()
        : this(Stopwatch.GetTimestamp, static ticks => Sdl3.SDL_DelayNS((ulong)(ticks * 1_000_000_000 / Stopwatch.Frequency)))
    {
    }

    // The seconds between frames the display asks for on this frame, 0 for no wait. Unfocused, the
    // lower cap applies. Minimized, the window presents nothing and wakes at most once a step, which
    // keeps the simulation on time. The development flag uncaps every case.
    internal static double PeriodSeconds(Display display, bool focused, bool minimized, double stepSeconds, bool uncapped)
    {
        if (uncapped)
        {
            return 0;
        }

        int cap = display.MaxFrameRate;
        int unfocused = display.UnfocusedMaxFrameRate;
        if (!focused && unfocused > 0 && (cap == 0 || unfocused < cap))
        {
            cap = unfocused;
        }

        double period = cap > 0 ? 1.0 / cap : 0;

        return minimized ? Math.Max(period, stepSeconds) : period;
    }

    // Returns once this frame is due, with the timestamp it starts at. A changed period starts a new
    // schedule at once.
    internal long Wait(double periodSeconds)
    {
        long now = timestamp();
        long period = (long)(periodSeconds * Stopwatch.Frequency);
        if (period != _period)
        {
            _period = period;
            _due = now;
        }

        if (period == 0)
        {
            return now;
        }

        long remaining = _due - now;
        if (remaining > 0)
        {
            sleep(remaining);
            now = timestamp();
        }

        _due = now - _due > period ? now + period : _due + period;

        return now;
    }
}
