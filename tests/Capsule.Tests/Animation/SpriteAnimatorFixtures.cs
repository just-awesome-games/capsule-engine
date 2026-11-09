using System.Numerics;
using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Animation;

internal static class SpriteAnimatorFixtures
{
    internal static readonly SpriteClip Walk = new(
        [Frame(0), Frame(1), Frame(2)],
        [2, 2, 2],
        loop: true);

    internal static readonly SpriteClip Land = new([Frame(3), Frame(4)], [1, 1]);

    // A one-tick first frame is the case a step that advanced too early would erase entirely.
    internal static readonly SpriteClip Blink = new([Frame(5), Frame(6)], [1, 3]);

    // A pose variant: Walk's shape exactly, different frames.
    internal static readonly SpriteClip WalkArmed = new(
        [Frame(10), Frame(11), Frame(12)],
        [2, 2, 2],
        loop: true);

    // An uneven, non-looping clip: the tick offset has to walk the durations, not divide by one.
    internal static readonly SpriteClip Shoot = new([Frame(20), Frame(21)], [4, 1]);

    // The frame the player would have seen after each of the run's first ticks.
    internal static Sprite[] DrawnOver(SimulationHost run, int ticks) =>
        AfterEachStep(run, ticks, () => run.Simulation.View.Sprites[0].Sprite);

    // What read returns after each of the run's next steps.
    internal static T[] AfterEachStep<T>(SimulationHost run, int steps, Func<T> read)
    {
        T[] seen = new T[steps];
        for (int step = 0; step < steps; step++)
        {
            run.Step();
            seen[step] = read();
        }

        return seen;
    }

    internal static (SpriteRenderer Renderer, SpriteAnimator Animator, SimulationHost Run) Animating()
    {
        SpriteRenderer renderer = new(Frame(9));
        SpriteAnimator animator = new(renderer);

        return (renderer, animator, Stepped(renderer, animator));
    }

    // Stepped through a scene, not by calling the component: an animator's whole promise is that it
    // advances on the fixed step, in the order a scene steps its components.
    internal static SimulationHost Stepped(params Component[] components)
    {
        SceneFixtures.Recorder entity = new("animated", []);
        foreach (Component component in components)
        {
            entity.Add(component);
        }

        return SceneFixtures.Simulate(entity);
    }

    internal static Sprite Frame(int index) => SceneFixtures.Frame(index);

    internal sealed class Animated : Entity
    {
        private readonly SpriteClip? _onStart;
        private readonly SpriteClip? _onStep;

        internal SpriteAnimator Animator { get; }

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

    // Plays from a component's step, every step. Any animator's Play of the clip already playing does nothing.
    internal sealed class Driver(Action play) : Component
    {
        protected internal override void OnStep(in StepContext context) => play();
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
