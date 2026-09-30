using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Animation.SpriteAnimatorFixtures;

namespace Capsule.Tests.Animation;

public sealed class SpriteAnimatorTickTests
{
    [Fact]
    public void TheTickCountsEveryEarlierFramesTicksAndThoseSpentOnTheFrameDrawn()
    {
        (_, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Shoot);

        // Frame 0 holds four ticks, so three steps in the cursor is three ticks into the first.
        run.Step(4);

        Assert.Equal(0, animator.FrameIndex);
        Assert.Equal(3, animator.Tick);

        run.Step();

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(4, animator.Tick);
    }

    [Fact]
    public void TheTickWrapsWithTheLoopRatherThanCountingOn()
    {
        (_, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Walk);

        // Seven steps over a six-tick loop: one tick into the second pass, not seven.
        run.Step(8);

        Assert.Equal(1, animator.Tick);
        Assert.Equal(0, animator.FrameIndex);
    }

    // The whole clip's ticks, so a variant played at it lands finished on the last frame too.
    [Fact]
    public void TheTickOfAFinishedClipIsItsTotalTicks()
    {
        (_, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Shoot);

        run.Step(20);

        Assert.True(animator.IsFinished);
        Assert.Equal(5, animator.Tick);
    }

    // Nine ticks into a five-tick reaction: the pose has to enter already spent, not replay.
    [Fact]
    public void PlayingAtATickPastANonLoopingClipIsFinishedOnItsLastFrame()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();

        animator.Play(Shoot, atTick: 9);

        Assert.True(animator.IsFinished);
        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(21), renderer.Sprite);

        run.Step(5);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(21), renderer.Sprite);
    }

    [Fact]
    public void PlayingAtATickInsideAFrameLeavesItTheRestOfItsTicks()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();

        animator.Play(Shoot, atTick: 2);

        Assert.Equal(0, animator.FrameIndex);
        Assert.Equal(Frame(20), renderer.Sprite);
        Assert.False(animator.IsFinished);

        // Two of frame 0's four ticks are already spent, so it holds for two steps and no more.
        run.Step(2);

        Assert.Equal(0, animator.FrameIndex);

        run.Step();

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(21), renderer.Sprite);
    }

    [Fact]
    public void PlayingALoopingClipAtATickWrapsTheOffset()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();

        // Fourteen ticks over a six-tick loop is tick 2: frame 1, freshly current.
        animator.Play(Walk, atTick: 14);

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(1), renderer.Sprite);
        Assert.False(animator.IsFinished);

        run.Step(3);

        Assert.Equal(2, animator.FrameIndex);
    }

    // A walk swapped for its shooting variant mid-stride, where the variant holds each frame longer.
    [Fact]
    public void PlayingAtAFrameKeepsTheFrameAndItsSpentTicksAcrossClipsOfDifferentTicks()
    {
        SpriteClip slowWalk = new([Frame(30), Frame(31), Frame(32)], [4, 4, 4], loop: true);
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Walk);
        run.Step(4);

        animator.PlayAtFrame(slowWalk, animator.FrameIndex, animator.FrameTick);

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(1, animator.FrameTick);
        Assert.Equal(Frame(31), renderer.Sprite);

        // One of the variant frame's four ticks is spent, so it holds for three more.
        run.Step(3);

        Assert.Equal(1, animator.FrameIndex);

        run.Step();

        Assert.Equal(2, animator.FrameIndex);
    }

    // A finished clip reads its last frame's full ticks, and that position has to go back in.
    [Fact]
    public void PlayingAtTheFrameAndFrameTickReadReproducesAFinishedClip()
    {
        (_, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Shoot);
        run.Step(20);

        animator.PlayAtFrame(Shoot, animator.FrameIndex, animator.FrameTick);

        Assert.True(animator.IsFinished);
        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(5, animator.Tick);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 4)]
    public void PlayingAtAFrameOutsideTheClipThrows(int frameIndex, int frameTick)
    {
        (_, SpriteAnimator animator, _) = Animating();

        Assert.Throws<ArgumentOutOfRangeException>(() => animator.PlayAtFrame(Shoot, frameIndex, frameTick));
    }

    // Only a non-looping clip can stand on its last frame with every tick spent.
    [Fact]
    public void PlayingALoopingClipWithItsLastFrameSpentThrows()
    {
        (_, SpriteAnimator animator, _) = Animating();

        Assert.Throws<ArgumentOutOfRangeException>(() => animator.PlayAtFrame(Walk, 2, 2));
    }
}
