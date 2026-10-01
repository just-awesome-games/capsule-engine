using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class EntityRotationRetentionTests
{
    // Each step retains at its top. A write interpolates from the last step's value, then settles.
    [Fact]
    public void ARotationWrittenDuringAStep_InterpolatesFromTheLastStepsValueThenSettles()
    {
        Spinner spinner = new();
        SceneFixtures.HookScene scene = new();
        scene.Add(spinner);
        SceneSimulation simulation = new(scene);

        spinner.WriteOnNextStep(1f);
        simulation.Step(SceneFixtures.Step(0));

        SpriteIntent turning = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(0f, turning.PreviousRotation);
        Assert.Equal(1f, turning.Rotation);

        spinner.WriteOnNextStep(2f);
        simulation.Step(SceneFixtures.Step(1));

        SpriteIntent turned = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(1f, turned.PreviousRotation);
        Assert.Equal(2f, turned.Rotation);

        simulation.Step(SceneFixtures.Step(2));

        SpriteIntent settled = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(2f, settled.PreviousRotation);
        Assert.Equal(2f, settled.Rotation);
    }

    // No step has retained anything for a value written before the first step, so the initial
    // frame shows it in place rather than turning up to it from zero.
    [Fact]
    public void ARotationWrittenOnStart_IsNotInterpolated()
    {
        Spinner spinner = new(onStart: 0.75f);
        SceneFixtures.HookScene scene = new();
        scene.Add(spinner);
        SceneSimulation simulation = new(scene);

        SpriteIntent initial = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(0.75f, initial.PreviousRotation);
        Assert.Equal(0.75f, initial.Rotation);

        simulation.Step(SceneFixtures.Step(0));

        SpriteIntent stepped = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(0.75f, stepped.PreviousRotation);
        Assert.Equal(0.75f, stepped.Rotation);
    }

    // The bounds a turned frame reports are its circle's box: an 8x8 frame about its centre reaches
    // its half-diagonal on every side, which is wider than the rect it draws at rest.
    [Fact]
    public void ASpriteOnATurnedEntity_ReportsItsCirclesBox()
    {
        Spinner spinner = new(onStart: 1f);
        SceneFixtures.HookScene scene = new();
        scene.Add(spinner);
        _ = new SceneSimulation(scene);

        Rect bounds = spinner.Renderer.Bounds;
        float reach = MathF.Sqrt(32f);

        Assert.Equal(new Vector2(10f, 20f) - new Vector2(reach, reach), new Vector2(bounds.Left, bounds.Top));
        Assert.Equal(new Vector2(10f, 20f) + new Vector2(reach, reach), new Vector2(bounds.Right, bounds.Bottom));
    }

    private sealed class Spinner : Entity
    {
        private readonly float _onStart;
        private float? _pending;

        internal Spinner(float onStart = 0f)
            : base(new Vector2(10f, 20f))
        {
            _onStart = onStart;
            Add(Renderer);
        }

        internal SpriteRenderer Renderer { get; } =
            new(new Sprite(SceneFixtures.Atlas, new TextureRegion(0, 0, 8, 8), new Vector2(4f, 4f)));

        internal void WriteOnNextStep(float rotation) => _pending = rotation;

        protected internal override void OnStart()
        {
            if (_onStart != 0f)
            {
                Rotation = _onStart;
            }
        }

        protected internal override void OnStep(in StepContext context)
        {
            if (_pending is { } rotation)
            {
                Rotation = rotation;
                _pending = null;
            }
        }
    }
}
