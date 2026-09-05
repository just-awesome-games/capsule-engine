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
        (SpriteRenderer renderer, SpriteAnimator animator, SceneSimulation simulation) = Animating();
        animator.Play(Walk);

        // Frame 0 is drawn for both of its own ticks, counted from the step Play preceded.
        Step(simulation, 2);

        Assert.Equal(0, animator.FrameIndex);
        Assert.Equal(Frame(0), renderer.Sprite);

        Step(simulation, 1);

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal(Frame(1), renderer.Sprite);

        Step(simulation, 3);

        // Six ticks of three two-tick frames leaves the last of them on its second tick.
        Assert.Equal(2, animator.FrameIndex);
        Assert.Equal(Frame(2), renderer.Sprite);
        Assert.False(animator.IsFinished);
    }

    [Fact]
    public void AClipThatDoesNotLoopFinishesAndHoldsItsLastFrame()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneSimulation simulation) = Animating();
        animator.Play(Land);

        Step(simulation, 3);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(4), renderer.Sprite);

        Step(simulation, 10);

        Assert.Equal(Frame(4), renderer.Sprite);
    }

    [Fact]
    public void PlayingTheClipAlreadyPlayingDoesNotRestartIt_UnlessAsked()
    {
        (_, SpriteAnimator animator, SceneSimulation simulation) = Animating();
        animator.Play(Walk);
        Step(simulation, 3);

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
        (SpriteRenderer renderer, SpriteAnimator animator, SceneSimulation simulation) = Animating();
        renderer.Offset = new Vector2(4, 8);
        renderer.Scale = new Vector2(1.4f, 0.6f);
        renderer.FlipX = true;
        renderer.Color = ColorRgba.Black;

        animator.Play(Walk);
        Step(simulation, 3);

        Assert.Equal(new Vector2(4, 8), renderer.Offset);
        Assert.Equal(new Vector2(1.4f, 0.6f), renderer.Scale);
        Assert.True(renderer.FlipX);
        Assert.Equal(ColorRgba.Black, renderer.Color);
    }

    [Fact]
    public void AnAnimatorWithNothingToPlayLeavesTheRenderersOwnFrameAlone()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneSimulation simulation) = Animating();

        Step(simulation, 5);

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
        SceneSimulation simulation = Simulate(entity);

        Assert.Equal([Frame(5), Frame(6), Frame(6), Frame(6), Frame(6)], DrawnOver(simulation, 5));
    }

    [Fact]
    public void AClipPlayedFromTheEntitysOwnStepDrawsItsFirstFrameForItsOwnTicks()
    {
        Animated entity = new(onStep: Blink);
        SceneSimulation simulation = Simulate(entity);

        // The entity asks every step; only the first is a change, and the rest are ignored.
        Assert.Equal([Frame(5), Frame(6), Frame(6), Frame(6), Frame(6)], DrawnOver(simulation, 5));
    }

    // The natural Capsule shape: the animator is driven by a component beside it, which the entity
    // attached second and the scene therefore steps after it. The Play reaches the animator only on
    // its following step, and counting the hold from there would draw this one-tick frame twice.
    [Fact]
    public void AClipPlayedByAComponentSteppedAfterTheAnimatorDrawsItsFirstFrameForItsOwnTicks()
    {
        Animated entity = new();
        entity.Add(new Driver(entity.Animator, Blink));
        SceneSimulation simulation = Simulate(entity);

        Assert.Equal([Frame(5), Frame(6), Frame(6), Frame(6), Frame(6)], DrawnOver(simulation, 5));
    }

    // A finished clip holds its last frame and is still the clip playing, so the state that started
    // it may keep asking for it; re-triggering it is restart.
    [Fact]
    public void AFinishedClipIsStillPlaying_AndRestartReplaysIt()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SceneSimulation simulation) = Animating();
        animator.Play(Land);
        Step(simulation, 3);
        Assert.True(animator.IsFinished);

        animator.Play(Land);

        Assert.True(animator.IsFinished);
        Assert.Equal(Frame(4), renderer.Sprite);

        animator.Play(Land, restart: true);

        Assert.False(animator.IsFinished);
        Assert.Equal(0, animator.FrameIndex);
        Assert.Equal(Frame(3), renderer.Sprite);
    }

    private static Sprite[] DrawnOver(SceneSimulation simulation, int ticks)
    {
        Sprite[] drawn = new Sprite[ticks];
        for (int tick = 0; tick < ticks; tick++)
        {
            simulation.Step(SceneFixtures.Step(tick));
            drawn[tick] = simulation.View.Sprites[0].Sprite;
        }

        return drawn;
    }

    private static SceneSimulation Simulate(Animated entity)
    {
        SceneFixtures.HookScene scene = new();
        scene.Add(entity);

        return new SceneSimulation(scene);
    }

    // Stepped through a scene, not by calling the component: the animator's whole promise is that
    // frames advance on the fixed step, in the order a scene steps its components.
    private static (SpriteRenderer Renderer, SpriteAnimator Animator, SceneSimulation Simulation) Animating()
    {
        SpriteRenderer renderer = new(Frame(9));
        SpriteAnimator animator = new(renderer);
        SceneFixtures.Recorder entity = new("animated", []);
        entity.Add(renderer);
        entity.Add(animator);

        SceneFixtures.HookScene scene = new();
        scene.Add(entity);

        return (renderer, animator, new SceneSimulation(scene));
    }

    private static void Step(SceneSimulation simulation, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            simulation.Step(SceneFixtures.Step(i));
        }
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
}
