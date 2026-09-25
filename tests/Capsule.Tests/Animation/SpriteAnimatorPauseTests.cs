using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.Animation.SpriteAnimatorFixtures;

namespace Capsule.Tests.Animation;

public sealed class SpriteAnimatorPauseTests
{
    // Shoot finishes five ticks in, so twenty held steps would have finished it many times over.
    [Fact]
    public void AHeldClipNeitherAdvancesNorFinishes()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Shoot);
        run.Step(3);

        animator.Paused = true;
        run.Step(20);

        Assert.Equal(2, animator.Tick);
        Assert.Equal(0, animator.FrameIndex);
        Assert.False(animator.IsFinished);
        Assert.Equal(Frame(20), renderer.Sprite);
    }

    [Fact]
    public void ClearingThePauseContinuesFromTheHeldTick()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Walk);
        run.Step(3);
        animator.Paused = true;
        run.Step(5);

        animator.Paused = false;

        // Held at tick 2, the middle frame's first tick. It has one tick left, then frame 2 draws.
        run.Step();

        Assert.Equal(3, animator.Tick);
        Assert.Equal(Frame(1), renderer.Sprite);

        run.Step();

        Assert.Equal(4, animator.Tick);
        Assert.Equal(Frame(2), renderer.Sprite);
    }

    [Fact]
    public void PlayingWhileHeldDrawsTheNewFrameStaysHeldAndAdvancesOnResume()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Walk);
        run.Step(3);
        animator.Paused = true;

        animator.Play(WalkArmed, atTick: animator.Tick);
        run.Step(10);

        Assert.True(animator.Paused);
        Assert.Same(WalkArmed, animator.Clip);
        Assert.Equal(2, animator.Tick);
        Assert.Equal(Frame(11), renderer.Sprite);

        // The held steps spent the step Play would have held its frame for, so resuming advances.
        animator.Paused = false;
        run.Step();

        Assert.Equal(3, animator.Tick);
    }
}
