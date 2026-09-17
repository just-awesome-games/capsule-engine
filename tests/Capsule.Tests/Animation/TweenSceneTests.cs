using System.Numerics;
using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Animation;

// A tween is only worth anything through an owner that applies it, so the contract is asserted where
// it lands: the frame a scene draws, one tick at a time, on the screen layer and in the world.
public sealed class TweenSceneTests
{
    private const int Ticks = 4;

    private static readonly Vector2 Canvas = new(120f, 60f);

    // Linear over four ticks: 255 * k / 4 rounded, the start included.
    private static readonly byte[] Ramp = [0, 64, 128, 191, 255];

    [Fact]
    public void AScreenFadeItsOwnerSteps_ReachesTheScreenLayerOneTickAtATime()
    {
        Scene scene = new();
        scene.Add(new Curtain());

        using SimulationHost run = new(scene, run: new Run { Canvas = Canvas });

        for (int tick = 0; tick <= Ticks; tick++)
        {
            if (tick > 0)
            {
                run.Step();
            }

            SpriteIntent drawn = Assert.Single(run.Simulation.View.ScreenSprites.ToArray());

            Assert.Equal(new ColorRgba(0, 0, 0, Ramp[tick]), drawn.Color);
            Assert.Equal(Canvas, drawn.Size);
        }
    }

    [Fact]
    public void AWorldTintItsOwnerSteps_ReachesTheWorldLayerOneTickAtATime()
    {
        Scene scene = new();
        scene.Add(new Flasher());

        using SimulationHost run = new(scene);

        for (int tick = 0; tick <= Ticks; tick++)
        {
            if (tick > 0)
            {
                run.Step();
            }

            SpriteIntent drawn = Assert.Single(run.Simulation.View.Sprites.ToArray());

            // Red back to white: the two channels the hit took out come back together.
            Assert.Equal(new ColorRgba(255, Ramp[tick], Ramp[tick], 255), drawn.Color);
        }
    }

    // Steps its tween and writes the result itself: nothing about the tween is registered with the
    // engine, and the component's own step is the only thing that advances it.
    private sealed class Fade(ColorRect rect, ColorRgba from, ColorRgba to, int ticks) : Component
    {
        private Tween _tween;

        protected internal override void OnStart()
        {
            _tween.Start(ticks);
            rect.Color = ColorRgba.Lerp(from, to, _tween.Value);
        }

        protected internal override void OnStep(in StepContext context)
        {
            _tween.Step();
            rect.Color = ColorRgba.Lerp(from, to, _tween.Value);
        }
    }

    private sealed class Tint(SpriteRenderer renderer, ColorRgba from, ColorRgba to, int ticks) : Component
    {
        private Tween _tween;

        protected internal override void OnStart()
        {
            _tween.Start(ticks);
            renderer.Color = ColorRgba.Lerp(from, to, _tween.Value);
        }

        protected internal override void OnStep(in StepContext context)
        {
            _tween.Step();
            renderer.Color = ColorRgba.Lerp(from, to, _tween.Value);
        }
    }

    private sealed class Curtain : ScreenEntity
    {
        private readonly ColorRect _rect = new(Vector2.One);

        internal Curtain()
            : base(Anchor.TopLeft, Vector2.Zero)
        {
            Add(_rect);
            Add(new Fade(_rect, new ColorRgba(0, 0, 0, 0), new ColorRgba(0, 0, 0, 255), Ticks));
        }

        // The canvas is a run constant, reached once the entity is in a scene.
        protected internal override void OnStart() => _rect.Size = Run.Canvas;
    }

    private sealed class Flasher : Entity
    {
        internal Flasher()
            : base(Vector2.Zero)
        {
            Scale = new Vector2(8f, 8f);
            SpriteRenderer renderer = new(Sprite.White);

            Add(renderer);
            Add(new Tint(renderer, ColorRgba.Red, ColorRgba.White, Ticks));
        }
    }
}
