using System.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

public sealed class FramePacerTests
{
    private const double FiftyAFrame = 1.0 / 50;
    private static readonly long Millisecond = Stopwatch.Frequency / 1000;

    // Every wake lands late, as a sleep does. The due times still fall one period apart, and no frame
    // drifts more than a millisecond from its place on the schedule.
    [Fact]
    public void Wait_HoldsTheCapsRateThoughEveryWakeRunsLate()
    {
        long now = 0;
        FramePacer pacer = new(() => now, sleep => now += sleep + (Millisecond * 7 / 10));

        long first = pacer.Wait(FiftyAFrame);
        for (int frame = 1; frame <= 100; frame++)
        {
            now += 5 * Millisecond;
            long started = pacer.Wait(FiftyAFrame);

            Assert.InRange(started - first, (frame * 20 * Millisecond) - Millisecond, (frame * 20 * Millisecond) + Millisecond);
        }
    }

    // A frame that ran long starts the schedule over. The frame after it keeps the cap's spacing instead
    // of running at once to make up the lost time.
    [Fact]
    public void Wait_StartsANewScheduleAfterAStallInsteadOfBursting()
    {
        long now = 0;
        FramePacer pacer = new(() => now, sleep => now += sleep);

        pacer.Wait(FiftyAFrame);
        now += 100 * Millisecond;
        long resumed = pacer.Wait(FiftyAFrame);
        long next = pacer.Wait(FiftyAFrame);

        Assert.Equal(100 * Millisecond, resumed);
        Assert.Equal(20 * Millisecond, next - resumed);
    }

    [Theory]
    [InlineData(true, false, 144, 30, false, 144)]
    [InlineData(false, false, 144, 30, false, 30)]
    [InlineData(false, false, 20, 30, false, 20)]
    [InlineData(false, false, 0, 0, false, 0)]
    [InlineData(false, true, 0, 0, false, 60)]
    [InlineData(false, true, 0, 30, false, 30)]
    [InlineData(false, true, 144, 30, true, 0)]
    public void PeriodSeconds_TakesTheLowerCapUnfocusedWakesAtMostOnceAStepMinimizedAndUncapsForTheFlag(
        bool focused, bool minimized, int maxFrameRate, int unfocusedMaxFrameRate, bool uncapped, int framesPerSecond)
    {
        Display display = new() { MaxFrameRate = maxFrameRate, UnfocusedMaxFrameRate = unfocusedMaxFrameRate };

        double period = FramePacer.PeriodSeconds(display, focused, minimized, 1.0 / 60, uncapped);

        Assert.Equal(framesPerSecond == 0 ? 0 : 1.0 / framesPerSecond, period, 12);
    }

    // A frame that waits for the display lasts at least one refresh, whatever its cap. An unknown
    // refresh rate adds nothing.
    [Theory]
    [InlineData(0, true, 60, 60)]
    [InlineData(240, true, 60, 60)]
    [InlineData(30, true, 60, 30)]
    [InlineData(0, false, 60, 0)]
    [InlineData(144, false, 60, 144)]
    [InlineData(0, true, 0, 0)]
    public void FrameSeconds_IsTheLongerOfTheCapsPeriodAndARefreshTheFrameWaitsFor(
        int capFramesPerSecond, bool waitsForRefresh, double refreshRate, int framesPerSecond)
    {
        double period = FramePacer.FrameSeconds(capFramesPerSecond == 0 ? 0 : 1.0 / capFramesPerSecond, waitsForRefresh, refreshRate);

        Assert.Equal(framesPerSecond == 0 ? 0 : 1.0 / framesPerSecond, period, 12);
    }
}
