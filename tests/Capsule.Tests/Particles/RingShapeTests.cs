using System.Numerics;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Particles;

public sealed class RingShapeTests
{
    private const float Radius = 10f;
    private const float Tolerance = 1e-4f;

    private static readonly Sprite Tile = SceneFixtures.Frame(4, 4);

    // Each call draws its own start. Every burst still steps evenly round the rim from it.
    [Theory]
    [InlineData(16, 2)]
    [InlineData(0, 5)]
    public void ABurstOnARing_SpreadsEvenlyFromOneStart(int directions, int count)
    {
        (ParticleEmitter emitter, SimulationHost host) = Build(directions > 0 ? EmitShape.Ring(Radius, directions) : EmitShape.Ring(Radius));
        host.Step();

        for (int burst = 0; burst < 4; burst++)
        {
            emitter.Emit(count);
            host.Step();

            Vector2[] points = [.. host.Simulation.View.Sprites.ToArray().TakeLast(count).Select(static intent => intent.Position)];
            for (int index = 0; index < count; index++)
            {
                Vector2 expected = Turn(points[0], index * MathF.Tau / count);
                Assert.True(Vector2.Distance(expected, points[index]) <= Tolerance, $"burst {burst}: expected {expected}, got {points[index]}");
                if (directions > 0)
                {
                    AssertOnDirection(points[index], directions);
                }
            }
        }
    }

    [Fact]
    public void RateSpawnsOnASnappedRing_LandOnlyOnItsDirections()
    {
        (ParticleEmitter emitter, SimulationHost host) = Build(EmitShape.Ring(Radius, 5));
        emitter.Rate = 600f;

        host.Step(3);

        SpriteIntent[] intents = host.Simulation.View.Sprites.ToArray();
        Assert.Equal(30, intents.Length);
        foreach (SpriteIntent intent in intents)
        {
            AssertOnDirection(intent.Position, 5);
        }
    }

    private static void AssertOnDirection(Vector2 point, int directions)
    {
        float turns = MathF.Atan2(point.Y, point.X) / MathF.Tau * directions;
        Assert.True(MathF.Abs(turns - MathF.Round(turns)) <= Tolerance, $"{point} is {turns} directions round");
        Assert.Equal(Radius, point.Length(), Tolerance);
    }

    private static Vector2 Turn(Vector2 point, float radians) =>
        Vector2.Transform(point, Matrix3x2.CreateRotation(radians));

    private static (ParticleEmitter Emitter, SimulationHost Host) Build(EmitShape shape)
    {
        ParticleEmitter emitter = new(Tile, capacity: 64) { Shape = shape, Lifetime = (5f, 5f) };
        Root root = new();
        root.Add(emitter);

        return (emitter, new SimulationHost(new SceneFixtures.HookScene(start: s => s.Add(root))));
    }

    private sealed class Root() : Entity(Vector2.Zero);
}
