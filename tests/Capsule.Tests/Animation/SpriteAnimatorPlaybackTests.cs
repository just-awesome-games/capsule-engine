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
        BeforeJoiningMidStep,
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
    public void AnAnimatorWithNothingToPlayLeavesTheRenderersOwnFrameAlone()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();

        run.Step(5);

        Assert.Null(animator.Clip);
        Assert.Equal(Frame(9), renderer.Sprite);
        Assert.Equal(Frame(9), animator.Frame);
    }

    // An animator that advanced on the step its clip started would retire this one-tick first frame
    // before a frame view held it. Wherever the Play came from, the hold counts from its tick. A pooled
    // entity played by another's step before it joins the scene counts from the tick it joins.
    [Theory]
    [InlineData(PlaySite.EntityStart)]
    [InlineData(PlaySite.EntityStep)]
    [InlineData(PlaySite.ComponentAfterTheAnimator)]
    [InlineData(PlaySite.BeforeJoiningMidStep)]
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

        SimulationHost run = site == PlaySite.BeforeJoiningMidStep ? Spawn(entity) : Simulate(entity);

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

        // The step the variant was played for spends nothing. Frame 1 was already one tick into its
        // two, and the step after retires it.
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

    private static SimulationHost Spawn(Animated entity)
    {
        SceneFixtures.HookScene scene = new();
        scene.Add(new Spawner(entity));

        return new SimulationHost(scene);
    }

    // Plays Blink on an entity out of the scene and adds it, on its own first step.
    private sealed class Spawner(Animated spawned) : Entity(System.Numerics.Vector2.Zero)
    {
        private bool _spawned;

        protected internal override void OnStep(in StepContext context)
        {
            if (_spawned)
            {
                return;
            }

            _spawned = true;
            spawned.Animator.Play(Blink);
            Scene.Add(spawned);
        }
    }
}
