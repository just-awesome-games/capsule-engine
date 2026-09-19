using System.Numerics;
using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Animation.SpriteAnimatorFixtures;

namespace Capsule.Tests.Animation;

public sealed class SpriteAnimatorPlaybackTests
{
    /// <summary>Where a <c>Play</c> is made from, which must not change the frames drawn.</summary>
    public enum PlaySite
    {
        EntityStart,
        EntityStep,
        ComponentAfterTheAnimator,
    }

    [Fact]
    public void PlayingDrawsTheFirstFrameBeforeAnyStepRuns()
    {
        SpriteRenderer renderer = new(Frame(9));
        SpriteAnimator animator = new(renderer);

        animator.Play(Walk);

        Assert.Equal(Frame(0), renderer.Sprite);
        Assert.Equal(Frame(0), animator.Frame);
        Assert.Equal(0, animator.FrameIndex);
        Assert.Same(Walk, animator.Clip);
    }

    [Fact]
    public void EachStepAdvancesOneTickAndWritesTheCurrentFrame()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Walk);

        // Frame 0 is drawn for both of its own ticks, counted from the step Play preceded.
        run.Step(2);

        Assert.Equal(0, animator.FrameIndex);
        Assert.Equal(Frame(0), renderer.Sprite);

        run.Step();

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(1), renderer.Sprite);

        run.Step(3);

        // Six ticks of three two-tick frames leaves the last of them on its second tick.
        Assert.Equal(2, animator.FrameIndex);
        Assert.Equal(Frame(2), renderer.Sprite);
        Assert.False(animator.IsFinished);
    }

    [Fact]
    public void AClipThatDoesNotLoopFinishesAndHoldsItsLastFrame()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Land);

        run.Step(3);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(4), renderer.Sprite);

        run.Step(10);

        Assert.Equal(Frame(4), renderer.Sprite);
    }

    [Fact]
    public void PlayingTheClipAlreadyPlayingDoesNotRestartIt_UnlessAsked()
    {
        (_, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Walk);
        run.Step(3);

        animator.Play(Walk);

        Assert.Equal(1, animator.FrameIndex);

        animator.Play(Walk, restart: true);

        Assert.Equal(0, animator.FrameIndex);
    }

    // Offset, flips and colour are the renderer's own, and an animator that reset them would undo
    // a facing every time it swapped a frame.
    [Fact]
    public void TheAnimatorWritesTheFrameAndNothingElseOnTheRenderer()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        renderer.Offset = new Vector2(4, 8);
        renderer.FlipX = true;
        renderer.Color = ColorRgba.Black;

        animator.Play(Walk);
        run.Step(3);

        Assert.Equal(new Vector2(4, 8), renderer.Offset);
        Assert.True(renderer.FlipX);
        Assert.Equal(ColorRgba.Black, renderer.Color);
    }

    [Fact]
    public void AnAnimatorWithNothingToPlayLeavesTheRenderersOwnFrameAlone()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();

        run.Step(5);

        Assert.Null(animator.Clip);
        Assert.Equal(Frame(9), renderer.Sprite);
        Assert.Equal(Frame(9), animator.Frame);
        Assert.False(animator.IsFinished);
    }

    // The frame view is what the player sees: an animator that advanced on the step its clip
    // started would retire this one-tick first frame before a single view held it. Wherever the
    // Play came from — an entity that asks every step, where only the first is a change, or the
    // natural Capsule shape of a component the entity attached after the animator, whose Play
    // reaches it only on the following step — the hold is counted from the tick Play ran in.
    [Theory]
    [InlineData(PlaySite.EntityStart)]
    [InlineData(PlaySite.EntityStep)]
    [InlineData(PlaySite.ComponentAfterTheAnimator)]
    public void AClipPlayedFromAnywhereDrawsItsFirstFrameForItsOwnTicks(PlaySite site)
    {
        Animated entity = site switch
        {
            PlaySite.EntityStart => new Animated(onStart: Blink),
            PlaySite.EntityStep => new Animated(onStep: Blink),
            _ => new Animated(),
        };

        if (site == PlaySite.ComponentAfterTheAnimator)
        {
            entity.Add(new Driver(entity.Animator, Blink));
        }

        SimulationHost run = Simulate(entity);

        Assert.Equal([Frame(5), Frame(6), Frame(6), Frame(6), Frame(6)], DrawnOver(run, 5));
    }

    // A finished clip holds its last frame and is still the clip playing, so the state that started
    // it may keep asking for it; re-triggering it is restart.
    [Fact]
    public void AFinishedClipIsStillPlaying_AndRestartReplaysIt()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Land);
        run.Step(3);
        Assert.True(animator.IsFinished);

        animator.Play(Land);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(4), renderer.Sprite);

        animator.Play(Land, restart: true);

        Assert.False(animator.IsFinished);
        Assert.Equal(0, animator.FrameIndex);
        Assert.Equal(Frame(3), renderer.Sprite);
    }

    // The point of playing a variant at the animator's own tick: the cursor is reproduced, so the
    // variant continues on the frame and the part-spent tick the outgoing clip stood on.
    [Fact]
    public void PlayingAVariantAtTheAnimatorsTickKeepsTheFrameAndTheTicksAlreadySpentOnIt()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Walk);
        run.Step(4);
        Assert.Equal(1, animator.FrameIndex);

        animator.Play(WalkArmed, atTick: animator.Tick);

        Assert.Same(WalkArmed, animator.Clip);
        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(11), renderer.Sprite);

        // The step the variant was played for spends nothing, and frame 1 was already one tick into
        // its two, so the step after that retires it — a restart would still be on frame 0 here.
        run.Step(2);

        Assert.Equal(2, animator.FrameIndex);
        Assert.Equal(Frame(12), renderer.Sprite);
    }

    // The variant comes from a component the entity attached after the animator, so the animator
    // has already stepped and written its own frame this tick; the variant must still be drawn.
    [Fact]
    public void PlayingAVariantDrawsItOnTheStepItIsAskedFor()
    {
        Animated entity = new(onStart: Walk);
        entity.Add(new Variant(entity.Animator, WalkArmed, onTick: 2));
        SimulationHost run = Simulate(entity);

        Assert.Equal(
            [Frame(0), Frame(0), Frame(11), Frame(11), Frame(12)],
            DrawnOver(run, 5));
    }

    [Fact]
    public void AFinishedClipIsStillFinishedInItsVariant()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Land);
        run.Step(3);
        Assert.True(animator.IsFinished);

        animator.Play(LandArmed, atTick: animator.Tick);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(14), renderer.Sprite);

        run.Step(5);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(14), renderer.Sprite);
    }
}
