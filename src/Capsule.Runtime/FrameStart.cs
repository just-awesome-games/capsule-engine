using System.Diagnostics;
using Capsule.Rendering;

namespace Capsule.Runtime;

// Holds a frame's start back from the moment the display can take it, and its input is sampled as late as the
// frame can still make its refresh. What a frame leaves spare is the time from the GPU finishing it to the display
// taking the next one. The hold grows while that stays above a margin and gives way at once when it falls short.
// A platform whose wait for the display does not block shows nothing spare, and its frames are never held. Times
// are Stopwatch ticks.
internal sealed class FrameStart(Func<long> timestamp, Action<long> sleep)
{
    // The least a frame keeps in hand for a sleep that wakes late and a frame that runs long. It keeps a quarter of
    // its own work where that is more, since a costlier frame swings further.
    private static readonly long LeastMargin = 2 * Stopwatch.Frequency / 1000;

    // How long the hold stops growing after a missed refresh, doubling up to the longest, and how long a run
    // without a miss starts that over.
    private static readonly long ShortestPause = Stopwatch.Frequency;
    private static readonly long LongestPause = 8 * Stopwatch.Frequency;

    private long _hold;
    private long _began;
    private long _woke;
    private long _growsFrom;
    private long _pause = ShortestPause;

    internal FrameStart()
        : this(Stopwatch.GetTimestamp, FramePacer.Sleep)
    {
    }

    // One refresh while each frame waits for one and no cap paces it slower, and 0 otherwise, when no refresh is
    // near enough to start a frame close to.
    internal static double PeriodSeconds(Display display, VSyncMode presented, bool minimized, double capPeriodSeconds)
    {
        double refresh = display.RefreshRate;
        bool waitsForRefresh = presented == VSyncMode.On && !minimized && refresh > 0;

        return display.LowLatency && waitsForRefresh && capPeriodSeconds <= 1 / refresh ? 1 / refresh : 0;
    }

    // The ticks the next frame is held for.
    internal long Holding => _hold;

    // Called once a frame with when the GPU finished it and when the display could take the next. Holds the next
    // frame for as long as the frames before it leave spare. A periodSeconds of 0 holds nothing.
    internal void Hold(long ready, long woke, double periodSeconds)
    {
        long period = (long)(periodSeconds * Stopwatch.Frequency);
        long interval = woke - _woke;
        _woke = woke;
        if (period <= 0)
        {
            _hold = 0;
            return;
        }

        long work = ready - _began;
        long spare = woke - ready;
        long margin = Math.Max(LeastMargin, work / 4);

        // A frame whose own CPU and GPU work ran past its refresh missed it with or without the hold.
        if (_hold > 0 && interval > period * 3 / 2 && work < period)
        {
            _hold /= 2;
            _pause = woke - _growsFrom > LongestPause ? ShortestPause : Math.Min(_pause * 2, LongestPause);
            _growsFrom = woke + _pause;
        }

        if (spare < margin)
        {
            _hold -= margin - spare;
        }
        else if (woke >= _growsFrom)
        {
            _hold += Math.Min((spare - margin) / 8, period / 128);
        }

        _hold = Math.Clamp(_hold, 0, Math.Max(0, period - margin - work));
        if (_hold > 0)
        {
            sleep(_hold);
        }

        _began = timestamp();
    }
}
