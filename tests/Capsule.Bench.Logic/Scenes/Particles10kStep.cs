using System.Numerics;
using Capsule.Animation;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;
using Capsule.Bench.Logic.Entities;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>10 000 particles at a full pool, headless: the step alone, with nothing drawn.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Particles10kStep : Scene
{
    public Particles10kStep()
    {
        Camera = new ParkedCamera();
        Add(new Holder(new ParticleEmitter(SpriteField.Tile, capacity: 10_000)
        {
            Shape = EmitShape.Rect(World.ViewportSize.X, World.ViewportSize.Y),
            Rate = 10_000f,
            Lifetime = new FloatRange(1f, 1f),
            Speed = new FloatRange(5f, 20f),
            Gravity = new Vector2(0f, 20f),
            ScaleOverLifetime = Curve.Linear(1f, 0.5f),
            Color = Gradient.Linear(ColorRgba.White, new ColorRgba(255, 255, 255, 0)),
            Blend = BlendMode.Additive,
            PrewarmSeconds = 1f,
        }));
    }
}
