using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Animation;
using Capsule.Scenes.Rendering;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Animation;

public sealed class SpriteAnimatorTests
{
    private static readonly TextureHandle Sheet = new("player", ".png");

    private static readonly SpriteClip Walk = new(
        [Frame(0), Frame(1), Frame(2)],
        [2, 2, 2],
        loop: true);

    private static readonly SpriteClip Land = new([Frame(3), Frame(4)], [1, 1]);

    // A one-tick first frame is the case a step that advanced too early would erase entirely.
    private static readonly SpriteClip Blink = new([Frame(5), Frame(6)], [1, 3]);

    // Pose variants: Walk's and Land's shape exactly, different frames.
    private static readonly SpriteClip WalkArmed = new(
        [Frame(10), Frame(11), Frame(12)],
        [2, 2, 2],
        loop: true);

    private static readonly SpriteClip LandArmed = new([Frame(13), Frame(14)], [1, 1]);

    // An uneven, non-looping clip: the tick offset has to walk the durations, not divide by one.
    private static readonly SpriteClip Shoot = new([Frame(20), Frame(21)], [4, 1]);

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
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();
        animator.Play(Walk);

        // Frame 0 is drawn for both of its own ticks, counted from the step Play preceded.
        run.Run(2);

        Assert.Equal(0, animator.FrameIndex);
        Assert.Equal(Frame(0), renderer.Sprite);

        run.Step();

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(1), renderer.Sprite);

        run.Run(3);

        // Six ticks of three two-tick frames leaves the last of them on its second tick.
        Assert.Equal(2, animator.FrameIndex);
        Assert.Equal(Frame(2), renderer.Sprite);
        Assert.False(animator.IsFinished);
    }

    [Fact]
    public void AClipThatDoesNotLoopFinishesAndHoldsItsLastFrame()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();
        animator.Play(Land);

        run.Run(3);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(4), renderer.Sprite);

        run.Run(10);

        Assert.Equal(Frame(4), renderer.Sprite);
    }

    [Fact]
    public void PlayingTheClipAlreadyPlayingDoesNotRestartIt_UnlessAsked()
    {
        (_, SpriteAnimator animator, SceneRun run) = Animating();
        animator.Play(Walk);
        run.Run(3);

        animator.Play(Walk);

        Assert.Equal(1, animator.FrameIndex);

        animator.Play(Walk, restart: true);

        Assert.Equal(0, animator.FrameIndex);
    }

    // Offset, scale, flips and colour are the renderer's own, and an animator that reset them would
    // undo a squash-and-stretch every time it swapped a frame.
    [Fact]
    public void TheAnimatorWritesTheFrameAndNothingElseOnTheRenderer()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();
        renderer.Offset = new Vector2(4, 8);
        renderer.Scale = new Vector2(1.4f, 0.6f);
        renderer.FlipX = true;
        renderer.Color = ColorRgba.Black;

        animator.Play(Walk);
        run.Run(3);

        Assert.Equal(new Vector2(4, 8), renderer.Offset);
        Assert.Equal(new Vector2(1.4f, 0.6f), renderer.Scale);
        Assert.True(renderer.FlipX);
        Assert.Equal(ColorRgba.Black, renderer.Color);
    }

    [Fact]
    public void AnAnimatorWithNothingToPlayLeavesTheRenderersOwnFrameAlone()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();

        run.Run(5);

        Assert.Null(animator.Clip);
        Assert.Equal(Frame(9), renderer.Sprite);
        Assert.Equal(Frame(9), animator.Frame);
        Assert.False(animator.IsFinished);
    }

    // The frame view is what the player sees: an animator that advanced on the step its clip
    // started would retire this one-tick first frame before a single view held it.
    [Fact]
    public void AClipPlayedFromOnStartDrawsItsFirstFrameForItsOwnTicks()
    {
        Animated entity = new(onStart: Blink);
        SceneRun run = Simulate(entity);

        Assert.Equal([Frame(5), Frame(6), Frame(6), Frame(6), Frame(6)], DrawnOver(run, 5));
    }

    [Fact]
    public void AClipPlayedFromTheEntitysOwnStepDrawsItsFirstFrameForItsOwnTicks()
    {
        Animated entity = new(onStep: Blink);
        SceneRun run = Simulate(entity);

        // The entity asks every step; only the first is a change, and the rest are ignored.
        Assert.Equal([Frame(5), Frame(6), Frame(6), Frame(6), Frame(6)], DrawnOver(run, 5));
    }

    // The natural Capsule shape: the animator is driven by a component beside it, which the entity
    // attached second and the scene therefore steps after it. The Play reaches the animator only on
    // its following step, and counting the hold from there would draw this one-tick frame twice.
    [Fact]
    public void AClipPlayedByAComponentSteppedAfterTheAnimatorDrawsItsFirstFrameForItsOwnTicks()
    {
        Animated entity = new();
        entity.Add(new Driver(entity.Animator, Blink));
        SceneRun run = Simulate(entity);

        Assert.Equal([Frame(5), Frame(6), Frame(6), Frame(6), Frame(6)], DrawnOver(run, 5));
    }

    // A finished clip holds its last frame and is still the clip playing, so the state that started
    // it may keep asking for it; re-triggering it is restart.
    [Fact]
    public void AFinishedClipIsStillPlaying_AndRestartReplaysIt()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();
        animator.Play(Land);
        run.Run(3);
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
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();
        animator.Play(Walk);
        run.Run(4);
        Assert.Equal(1, animator.FrameIndex);

        animator.Play(WalkArmed, atTick: animator.Tick);

        Assert.Same(WalkArmed, animator.Clip);
        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(11), renderer.Sprite);

        // The step the variant was played for spends nothing, and frame 1 was already one tick into
        // its two, so the step after that retires it — a restart would still be on frame 0 here.
        run.Run(2);

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
        SceneRun run = Simulate(entity);

        Assert.Equal(
            [Frame(0), Frame(0), Frame(11), Frame(11), Frame(12)],
            DrawnOver(run, 5));
    }

    [Fact]
    public void AFinishedClipIsStillFinishedInItsVariant()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();
        animator.Play(Land);
        run.Run(3);
        Assert.True(animator.IsFinished);

        animator.Play(LandArmed, atTick: animator.Tick);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(14), renderer.Sprite);

        run.Run(5);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(14), renderer.Sprite);
    }

    [Fact]
    public void TheTickIsZeroUntilAClipPlays()
    {
        (_, SpriteAnimator animator, SceneRun run) = Animating();

        Assert.Equal(0, animator.Tick);

        run.Run(3);

        Assert.Equal(0, animator.Tick);
    }

    [Fact]
    public void TheTickCountsEveryEarlierFramesTicksAndThoseSpentOnTheFrameDrawn()
    {
        (_, SpriteAnimator animator, SceneRun run) = Animating();
        animator.Play(Shoot);

        // Frame 0 holds four ticks, so three steps in the cursor is three ticks into the first.
        run.Run(4);

        Assert.Equal(0, animator.FrameIndex);
        Assert.Equal(3, animator.Tick);

        run.Step();

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(4, animator.Tick);
    }

    [Fact]
    public void TheTickWrapsWithTheLoopRatherThanCountingOn()
    {
        (_, SpriteAnimator animator, SceneRun run) = Animating();
        animator.Play(Walk);

        // Seven steps over a six-tick loop: one tick into the second pass, not seven.
        run.Run(8);

        Assert.Equal(1, animator.Tick);
        Assert.Equal(0, animator.FrameIndex);
    }

    // The whole clip's ticks, so a variant played at it lands finished on the last frame too.
    [Fact]
    public void TheTickOfAFinishedClipIsItsTotalTicks()
    {
        (_, SpriteAnimator animator, SceneRun run) = Animating();
        animator.Play(Shoot);

        run.Run(20);

        Assert.True(animator.IsFinished);
        Assert.Equal(5, animator.Tick);
    }

    // Nine ticks into a five-tick reaction: the pose has to enter already spent, not replay.
    [Fact]
    public void PlayingAtATickPastANonLoopingClipIsFinishedOnItsLastFrame()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();

        animator.Play(Shoot, atTick: 9);

        Assert.True(animator.IsFinished);
        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(21), renderer.Sprite);

        run.Run(5);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(21), renderer.Sprite);
    }

    [Fact]
    public void PlayingAtATickInsideAFrameLeavesItTheRestOfItsTicks()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();

        animator.Play(Shoot, atTick: 2);

        Assert.Equal(0, animator.FrameIndex);
        Assert.Equal(Frame(20), renderer.Sprite);
        Assert.False(animator.IsFinished);

        // Two of frame 0's four ticks are already spent, so it holds for two steps and no more.
        run.Run(2);

        Assert.Equal(0, animator.FrameIndex);

        run.Step();

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(21), renderer.Sprite);
    }

    [Fact]
    public void PlayingALoopingClipAtATickWrapsTheOffset()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneRun run) = Animating();

        // Fourteen ticks over a six-tick loop is tick 2: frame 1, freshly current.
        animator.Play(Walk, atTick: 14);

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(1), renderer.Sprite);
        Assert.False(animator.IsFinished);

        run.Run(3);

        Assert.Equal(2, animator.FrameIndex);
    }

    // The frame the player would have seen after each of the run's first ticks.
    private static Sprite[] DrawnOver(SceneRun run, int ticks)
    {
        Sprite[] drawn = new Sprite[ticks];
        for (int tick = 0; tick < ticks; tick++)
        {
            run.Step();
            drawn[tick] = run.Simulation.View.Sprites[0].Sprite;
        }

        return drawn;
    }

    private static SceneRun Simulate(Animated entity)
    {
        SceneFixtures.HookScene scene = new();
        scene.Add(entity);

        return new SceneRun(scene);
    }

    // Stepped through a scene, not by calling the component: the animator's whole promise is that
    // frames advance on the fixed step, in the order a scene steps its components.
    private static (SpriteRenderer Renderer, SpriteAnimator Animator, SceneRun Run) Animating()
    {
        SpriteRenderer renderer = new(Frame(9));
        SpriteAnimator animator = new(renderer);
        SceneFixtures.Recorder entity = new("animated", []);
        entity.Add(renderer);
        entity.Add(animator);

        SceneFixtures.HookScene scene = new();
        scene.Add(entity);

        return (renderer, animator, new SceneRun(scene));
    }

    private static Sprite Frame(int index) =>
        new(Sheet, new TextureRegion(index * 8, 0, 8, 8), new Vector2(4, 8));

    private sealed class Animated : Entity
    {
        private readonly SpriteClip? _onStart;
        private readonly SpriteClip? _onStep;

        internal Animated(SpriteClip? onStart = null, SpriteClip? onStep = null)
            : base(Vector2.Zero)
        {
            _onStart = onStart;
            _onStep = onStep;

            SpriteRenderer renderer = new(Frame(9));
            Animator = new SpriteAnimator(renderer);
            Add(renderer);
            Add(Animator);
        }

        internal SpriteAnimator Animator { get; }

        protected internal override void OnStart()
        {
            if (_onStart is { } clip)
            {
                Animator.Play(clip);
            }
        }

        protected internal override void OnStep(in StepContext context)
        {
            if (_onStep is { } clip)
            {
                Animator.Play(clip);
            }
        }
    }

    private sealed class Driver(SpriteAnimator animator, SpriteClip clip) : Component
    {
        protected internal override void OnStep(in StepContext context) => animator.Play(clip);
    }

    private sealed class Variant(SpriteAnimator animator, SpriteClip clip, long onTick) : Component
    {
        protected internal override void OnStep(in StepContext context)
        {
            if (context.Tick >= onTick)
            {
                animator.Play(clip, atTick: animator.Tick);
            }
        }
    }
}
