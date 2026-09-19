using Capsule.Animation;

namespace Capsule.Tests.Animation;

public sealed class CountdownTests
{
    // The edge is the countdown's reason to exist: an owner reading JustFinished once per step runs
    // its elapsed branch exactly once per run, and never for a run that was cancelled.
    [Fact]
    public void TheStepThatSpendsTheLastTick_IsTheOnlyOneThatReportsTheFinish()
    {
        Countdown countdown = default;
        countdown.Start(3);

        List<bool> edges = [];
        for (int step = 0; step < 5; step++)
        {
            countdown.Step();
            edges.Add(countdown.JustFinished);
        }

        Assert.Equal([false, false, true, false, false], edges);
        Assert.Equal(3, countdown.Duration);
        Assert.Equal(3, countdown.TicksElapsed);
        Assert.Equal(0, countdown.TicksLeft);
        Assert.False(countdown.IsRunning);
    }

    [Fact]
    public void AStoppedCountdown_ReportsNoFinishAndLeavesItsDurationToRestartOn()
    {
        Countdown countdown = default;
        countdown.Start(2);
        countdown.Step();

        countdown.Stop();
        countdown.Step();

        Assert.False(countdown.JustFinished);
        Assert.False(countdown.IsRunning);
        Assert.Equal(0, countdown.TicksLeft);
        Assert.Equal(2, countdown.Duration);

        countdown.Start(2);

        Assert.Equal(2, countdown.TicksLeft);
        Assert.True(countdown.IsRunning);
    }

    [Fact]
    public void ACountdownOfNoTicks_IsFinishedAtOnceAndNeverReportsAnEdge()
    {
        Countdown countdown = default;
        countdown.Start(5);
        countdown.Start(0);

        Assert.False(countdown.IsRunning);
        Assert.False(countdown.JustFinished);
        Assert.Equal(0, countdown.Duration);

        countdown.Step();

        Assert.False(countdown.JustFinished);
    }
}
