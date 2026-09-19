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
}
