namespace Capsule.Tests.Runtime;

// The countdown's whole reason to exist is the single edge: an owner reading JustFinished once per
// step must run its elapsed branch exactly once per run, and never for a run that was cancelled.
public sealed class CountdownTests
{
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
        Assert.Equal(0, countdown.TicksLeft);
        Assert.Equal(3, countdown.TicksElapsed);
        Assert.False(countdown.IsRunning);
    }

    [Fact]
    public void ACountdownStopped_ReportsNoFinishOnTheStepThatWouldHaveSpentItsLastTick()
    {
        Countdown countdown = default;
        countdown.Start(1);

        countdown.Stop();
        countdown.Step();

        Assert.False(countdown.JustFinished);
        Assert.False(countdown.IsRunning);

        // The duration survives, so the owner can rearm on it, and a stop leaves nothing to spend:
        // the elapsed count reads the whole duration though only the stop got it there.
        Assert.Equal(1, countdown.Duration);
        Assert.Equal(1, countdown.TicksElapsed);
    }

    [Fact]
    public void ACountdownOfNoTicks_IsFinishedAtOnceAndNeverReportsAnEdge()
    {
        Countdown countdown = default;
        countdown.Start(0);

        Assert.False(countdown.IsRunning);
        Assert.False(countdown.JustFinished);

        countdown.Step();

        Assert.False(countdown.JustFinished);
    }

    [Fact]
    public void RestartingWithinTheEdgeStep_ClearsIt()
    {
        Countdown countdown = default;
        countdown.Start(1);
        countdown.Step();

        countdown.Start(2);

        Assert.False(countdown.JustFinished);
        Assert.Equal(2, countdown.TicksLeft);
    }

    [Fact]
    public void ACountdownOfNegativeTicks_IsRefused()
    {
        Countdown countdown = default;

        Assert.Throws<ArgumentOutOfRangeException>(() => countdown.Start(-1));
    }
}
