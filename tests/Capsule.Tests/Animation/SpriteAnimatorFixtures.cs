using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Animation;

internal static class SpriteAnimatorFixtures
{
    internal static readonly TextureHandle Sheet = new("player", ".png");

    internal static readonly SpriteClip Walk = new(
        [Frame(0), Frame(1), Frame(2)],
        [2, 2, 2],
        loop: true);

    internal static readonly SpriteClip Land = new([Frame(3), Frame(4)], [1, 1]);

    // A one-tick first frame is the case a step that advanced too early would erase entirely.
    internal static readonly SpriteClip Blink = new([Frame(5), Frame(6)], [1, 3]);

    // Pose variants: Walk's and Land's shape exactly, different frames.
    internal static readonly SpriteClip WalkArmed = new(
        [Frame(10), Frame(11), Frame(12)],
        [2, 2, 2],
        loop: true);

    internal static readonly SpriteClip LandArmed = new([Frame(13), Frame(14)], [1, 1]);

    // An uneven, non-looping clip: the tick offset has to walk the durations, not divide by one.
    internal static readonly SpriteClip Shoot = new([Frame(20), Frame(21)], [4, 1]);

    // The frame the player would have seen after each of the run's first ticks.
    internal static Sprite[] DrawnOver(SimulationHost run, int ticks)
    {
        Sprite[] drawn = new Sprite[ticks];
        for (int tick = 0; tick < ticks; tick++)
        {
            run.Step();
            drawn[tick] = run.Simulation.View.Sprites[0].Sprite;
        }

        return drawn;
    }

    internal static SimulationHost Simulate(Animated entity)
    {
        SceneFixtures.HookScene scene = new();
        scene.Add(entity);

        return new SimulationHost(scene);
    }

    // Stepped through a scene, not by calling the component: the animator's whole promise is that
    // frames advance on the fixed step, in the order a scene steps its components.
    internal static (SpriteRenderer Renderer, SpriteAnimator Animator, SimulationHost Run) Animating()
    {
        SpriteRenderer renderer = new(Frame(9));
        SpriteAnimator animator = new(renderer);
        SceneFixtures.Recorder entity = new("animated", []);
        entity.Add(renderer);
        entity.Add(animator);

        SceneFixtures.HookScene scene = new();
        scene.Add(entity);

        return (renderer, animator, new SimulationHost(scene));
    }

    internal static Sprite Frame(int index) =>
        new(Sheet, new TextureRegion(index * 8, 0, 8, 8), new Vector2(4, 8));

    internal sealed class Animated : Entity
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

    internal sealed class Driver(SpriteAnimator animator, SpriteClip clip) : Component
    {
        protected internal override void OnStep(in StepContext context) => animator.Play(clip);
    }

    internal sealed class Variant(SpriteAnimator animator, SpriteClip clip, long onTick) : Component
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
