using System.Numerics;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Particles;

public sealed class ParticleSpaceTests
{
    private const float Tolerance = 1e-4f;

    private static readonly Sprite Tile = SceneFixtures.Frame(4, 4);

    [Fact]
    public void ALocalParticle_KeepsItsDrawnOffsetFromAMovingMirroredEntity()
    {
        Driven entity = new(new Vector2(1f, 0.5f), 0f) { Scale = new Vector2(-1f, 1f) };
        ParticleEmitter emitter = new(Tile, capacity: 1) { Space = ParticleSpace.Local, Lifetime = (5f, 5f) };
        entity.Add(emitter);
        SimulationHost host = new(new SceneFixtures.HookScene(start: s => s.Add(entity)));

        emitter.Emit(1, new Vector2(3f, 2f));

        for (int step = 0; step < 5; step++)
        {
            host.Step();

            SpriteIntent intent = host.Simulation.View.Sprites.ToArray().Single();
            AssertClose(new Vector2(-3f, 2f), intent.PreviousPosition - entity.PreviousWorld.Position);
            AssertClose(new Vector2(-3f, 2f), intent.Position - entity.WorldTransform.Position);
        }
    }

    [Fact]
    public void Gravity_PullsWorldDownOnATurnedMirroredLocalEmitter()
    {
        Driven entity = new(Vector2.Zero, 0f) { Rotation = 1f, Scale = new Vector2(-1f, 1f) };
        ParticleEmitter emitter = new(Tile, capacity: 1)
        {
            Space = ParticleSpace.Local,
            Lifetime = (5f, 5f),
            Gravity = new Vector2(0f, 100f),
        };
        entity.Add(emitter);
        SimulationHost host = new(new SceneFixtures.HookScene(start: s => s.Add(entity)));

        emitter.Emit(1);
        host.Step(10);

        Vector2 fall = host.Simulation.View.Sprites.ToArray().Single().Position - entity.WorldTransform.Position;
        Assert.True(fall.Y > 0.1f, $"fell {fall}");
        Assert.True(MathF.Abs(fall.X) <= Tolerance, $"fell {fall}");
    }

    // A ring spawn steps inward by the speed each step. A centre spawn has no line to move along.
    [Theory]
    [InlineData(10f, 0.5f)]
    [InlineData(0f, 0f)]
    public void NegativeRadialSpeed_MovesASpawnTowardTheCentre(float ringRadius, float expectedStep)
    {
        Driven entity = new(Vector2.Zero, 0f);
        ParticleEmitter emitter = new(Tile, capacity: 1)
        {
            Shape = ringRadius > 0f ? EmitShape.Ring(ringRadius) : EmitShape.Point,
            RadialSpeed = (-30f, -30f),
            Lifetime = (5f, 5f),
        };
        entity.Add(emitter);
        SimulationHost host = new(new SceneFixtures.HookScene(start: s => s.Add(entity)));

        emitter.Emit(1);
        host.Step(3);

        SpriteIntent intent = host.Simulation.View.Sprites.ToArray().Single();
        Assert.Equal(expectedStep, (intent.Position - intent.PreviousPosition).Length(), Tolerance);
        Assert.Equal(expectedStep, intent.PreviousPosition.Length() - intent.Position.Length(), Tolerance);
    }

    [Fact]
    public void LocalBounds_CoverEveryDrawnEndpointOnATurnedMovingEntity()
    {
        Driven entity = new(new Vector2(5f, 2f), 0.3f);
        ParticleEmitter emitter = new(Tile, capacity: 32)
        {
            Space = ParticleSpace.Local,
            Shape = EmitShape.Ring(10f),
            Offset = new Vector2(12f, 0f),
            Speed = (10f, 40f),
            Spread = 360f,
            Rate = 120f,
            Lifetime = (0.5f, 0.5f),
        };
        entity.Add(emitter);
        SimulationHost host = new(new SceneFixtures.HookScene(start: s => s.Add(entity)));

        for (int step = 0; step < 20; step++)
        {
            host.Step();

            Rect bounds = emitter.Bounds;
            foreach (SpriteIntent intent in host.Simulation.View.Sprites.ToArray())
            {
                Assert.True(Covers(bounds, intent.PreviousPosition), $"{bounds} misses {intent.PreviousPosition}");
                Assert.True(Covers(bounds, intent.Position), $"{bounds} misses {intent.Position}");
            }
        }
    }

    private static bool Covers(Rect rect, Vector2 point) =>
        point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;

    private static void AssertClose(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) <= Tolerance, $"expected {expected}, got {actual}");

    // Moves and turns by a fixed amount every step.
    private sealed class Driven(Vector2 velocityPerStep, float turnPerStep) : Entity(Vector2.Zero)
    {
        protected internal override void OnStep(in StepContext context)
        {
            Position += velocityPerStep;
            Rotation += turnPerStep;
        }
    }
}
