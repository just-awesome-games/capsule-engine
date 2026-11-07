using System.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

public sealed class FrameStartTests
{
    private const double SixtyAFrame = 1.0 / 60;
    private static readonly long Millisecond = Stopwatch.Frequency / 1000;
    private static readonly long Refresh = (long)(SixtyAFrame * Stopwatch.Frequency);

    // The hold settles where a frame keeps its margin of the refresh, whatever its work is, CPU or GPU. A frame of
    // sixteen milliseconds keeps a quarter of its work and has none to give.
    [Theory]
    [InlineData(1, 0, 13.67)]
    [InlineData(1, 8, 5.42)]
    [InlineData(1, 15, 0)]
    public void Hold_SettlesAtTheMarginAndMissesNoRefresh(int cpuMilliseconds, int gpuMilliseconds, double settledMilliseconds)
    {
        Display60 display = new();

        display.Run(frames: 2000, cpuMilliseconds * Millisecond, gpuMilliseconds * Millisecond);

        Assert.InRange(display.Start.Holding / (double)Millisecond, settledMilliseconds - 0.3, settledMilliseconds + 0.3);
        Assert.Equal(0, display.MissedSince(frame: 1000));
    }

    // A wait that returns at once leaves nothing spare, as a platform whose wait does not block does.
    [Fact]
    public void Hold_NeverHoldsAFrameWhoseWaitDoesNotBlock()
    {
        long now = 0;
        FrameStart start = new(() => now, sleep => now += sleep);

        for (int frame = 0; frame < 500; frame++)
        {
            now += Millisecond;
            start.Hold(now, now, SixtyAFrame);

            Assert.Equal(0, start.Holding);
        }
    }

    // A refresh missed while holding halves the hold, which stays put for a second before it grows again.
    [Fact]
    public void Hold_BacksOffAMissedRefreshAndPausesGrowth()
    {
        Display60 display = new();
        display.Run(frames: 1000, Millisecond, gpu: 0);
        long settled = display.Start.Holding;

        display.StallDisplayOneRefresh();
        Assert.InRange(display.Start.Holding, (settled / 2) - Millisecond, (settled / 2) + Millisecond);

        long afterMiss = display.Start.Holding;
        display.Run(frames: 50, Millisecond, gpu: 0);
        Assert.Equal(afterMiss, display.Start.Holding);

        display.Run(frames: 300, Millisecond, gpu: 0);
        Assert.True(display.Start.Holding > afterMiss, "The hold grows again once the pause ends.");
    }

    // A frame whose own work outran the refresh missed it with or without the hold. The hold gives way for it and
    // grows straight back.
    [Fact]
    public void Hold_DoesNotPauseForAFrameThatOutranTheRefreshOnItsOwn()
    {
        Display60 display = new();
        display.Run(frames: 1000, Millisecond, gpu: 0);

        display.Run(frames: 1, 40 * Millisecond, gpu: 0);
        Assert.Equal(0, display.Start.Holding);

        display.Run(frames: 50, Millisecond, gpu: 0);
        Assert.True(display.Start.Holding > 0);
    }

    [Theory]
    [InlineData(true, VSyncMode.On, false, 60d, 0d, 60d)]
    [InlineData(true, VSyncMode.On, false, 60d, 1d / 60, 60d)]
    [InlineData(true, VSyncMode.On, false, 60d, 1d / 240, 60d)]
    [InlineData(true, VSyncMode.On, false, 60d, 1d / 30, 0d)]
    [InlineData(true, VSyncMode.On, false, 0d, 0d, 0d)]
    [InlineData(true, VSyncMode.On, true, 60d, 0d, 0d)]
    [InlineData(true, VSyncMode.Mailbox, false, 60d, 0d, 0d)]
    [InlineData(true, VSyncMode.Off, false, 60d, 0d, 0d)]
    [InlineData(false, VSyncMode.On, false, 60d, 0d, 0d)]
    public void PeriodSeconds_IsARefreshOnlyWhileFramesWaitForOne(bool lowLatency, VSyncMode presented, bool minimized, double refresh, double capPeriodSeconds, double heldHertz)
    {
        Display display = new Run().Display;
        display.LowLatency = lowLatency;
        display.RefreshRate = refresh;

        Assert.Equal(heldHertz > 0 ? 1 / heldHertz : 0, FrameStart.PeriodSeconds(display, presented, minimized, capPeriodSeconds));
    }

    // A fixed-rate display: a frame the GPU finishes is taken at the next refresh, and the frame after it starts there.
    private sealed class Display60
    {
        private readonly List<long> _intervals = [];
        private long _now;
        private long _lastWoke;

        internal FrameStart Start { get; }

        internal Display60()
        {
            Start = new FrameStart(() => _now, sleep => _now += sleep);
        }

        internal void Run(int frames, long cpu, long gpu)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                _now += cpu;
                long ready = _now + gpu;
                long woke = ((ready / Refresh) + 1) * Refresh;
                _now = woke;
                _intervals.Add(woke - _lastWoke);
                _lastWoke = woke;
                Start.Hold(ready, woke, SixtyAFrame);
            }
        }

        // The display takes the next frame a refresh late, through nothing the frame did.
        internal void StallDisplayOneRefresh()
        {
            long ready = _now + Millisecond;
            long woke = ((ready / Refresh) + 2) * Refresh;
            _now = woke;
            _lastWoke = woke;
            Start.Hold(ready, woke, SixtyAFrame);
        }

        internal int MissedSince(int frame) => _intervals.Skip(frame).Count(static interval => interval > Refresh * 3 / 2);
    }
}
