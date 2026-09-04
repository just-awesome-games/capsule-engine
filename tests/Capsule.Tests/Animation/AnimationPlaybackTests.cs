using Capsule.Animation;

namespace Capsule.Tests.Animation;

public sealed class AnimationPlaybackTests
{
    private static readonly int[] Three = [3, 1, 2];

    [Fact]
    public void AFreshCursorIsOnTheFirstFrameWithNothingElapsed()
    {
        AnimationPlayback playback = default;

        Assert.Equal(0, playback.FrameIndex);
        Assert.Equal(0, playback.TicksElapsed);
        Assert.False(playback.IsFinished);
    }

    // The whole contract in one walk: a frame of n ticks is current across exactly n steps.
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 2)]
    [InlineData(3, 1, 0)]
    [InlineData(4, 2, 0)]
    [InlineData(5, 2, 1)]
    public void EachFrameIsCurrentForExactlyItsOwnTicks(int steps, int frame, int elapsed)
    {
        AnimationPlayback playback = default;

        for (int i = 0; i < steps; i++)
        {
            playback.Step(Three, loop: false);
        }

        Assert.Equal(frame, playback.FrameIndex);
        Assert.Equal(elapsed, playback.TicksElapsed);
    }

    [Fact]
    public void ALoopingRunWrapsToTheFirstFrameAndNeverFinishes()
    {
        AnimationPlayback playback = default;

        for (int i = 0; i < 6; i++)
        {
            playback.Step(Three, loop: true);
        }

        Assert.Equal(0, playback.FrameIndex);
        Assert.Equal(0, playback.TicksElapsed);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void ARunThatDoesNotLoopHoldsItsLastFrameAndFinishesOnItsLastTick()
    {
        AnimationPlayback playback = default;

        for (int i = 0; i < 5; i++)
        {
            playback.Step(Three, loop: false);
        }

        Assert.False(playback.IsFinished);

        playback.Step(Three, loop: false);

        Assert.True(playback.IsFinished);
        Assert.Equal(2, playback.FrameIndex);

        // Stepping a finished run is a no-op, so the last frame keeps drawing.
        playback.Step(Three, loop: false);

        Assert.True(playback.IsFinished);
        Assert.Equal(2, playback.FrameIndex);
        Assert.Equal(2, playback.TicksElapsed);
    }

    [Fact]
    public void RestartReturnsAFinishedCursorToTheFirstFrame()
    {
        AnimationPlayback playback = default;
        for (int i = 0; i < 6; i++)
        {
            playback.Step(Three, loop: false);
        }

        playback.Restart();

        Assert.Equal(0, playback.FrameIndex);
        Assert.Equal(0, playback.TicksElapsed);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void SteppingOverARunTheCursorHasOutgrownIsRefused()
    {
        AnimationPlayback playback = default;
        playback.Step(Three, loop: false);
        playback.Step(Three, loop: false);
        playback.Step(Three, loop: false);

        Assert.Throws<ArgumentException>(() => StepOnce(playback, [4]));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { -1 })]
    public void ARunWithNoFrameToHoldIsRefused(int[] frameTicks)
    {
        AnimationPlayback playback = default;

        Assert.Throws<ArgumentException>(() => StepOnce(playback, frameTicks));
    }

    // By value, not by reference: a ref local cannot be captured, and a throwing step advances
    // nothing worth keeping.
    private static void StepOnce(AnimationPlayback playback, int[] frameTicks) =>
        playback.Step(frameTicks, loop: false);
}
