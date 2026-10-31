using System.Numerics;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Particles.ParticleCollisionTests;

namespace Capsule.Tests.Particles;

public sealed class ParticleNoiseTests
{
    // A push with no divergence neither gathers particles into a point nor thins them from one. It holds only where
    // the slope's two axes are the partials of one field. A slope wrong on either axis fails it.
    [Fact]
    public void ThePush_HasNoDivergence_AndIsNotStill()
    {
        const float H = 1e-2f;
        float largestTurn = 0f;
        for (int x = -3; x < 3; x++)
        {
            for (int y = -3; y < 3; y++)
            {
                Vector2 point = new(x + 0.2f + (0.3f * ((x + y) & 1)), y + 0.35f);
                float pushXAlongX = (Push(point + new Vector2(H, 0f)).X - Push(point - new Vector2(H, 0f)).X) / (2f * H);
                float pushYAlongY = (Push(point + new Vector2(0f, H)).Y - Push(point - new Vector2(0f, H)).Y) / (2f * H);

                Assert.True(MathF.Abs(pushXAlongX + pushYAlongY) <= 1e-2f, $"divergence {pushXAlongX + pushYAlongY} at {point}");
                largestTurn = MathF.Max(largestTurn, MathF.Abs(pushXAlongX));
            }
        }

        Assert.True(largestTurn > 0.1f);
    }

    [Fact]
    public void TwoEmittersOnOnePoint_SwirlApart_NeverFasterThanTheStrengthOnEitherAxis()
    {
        const float Strength = 30f;
        ParticleEmitter first = Swirling(SceneFixtures.Frame(4, 4), Strength);
        ParticleEmitter second = Swirling(SceneFixtures.Frame(2, 2), Strength);
        SceneFixtures.HookScene scene = new();
        scene.Add(Holding(first, new Vector2(8f, 8f)));
        scene.Add(Holding(second, new Vector2(8f, 8f)));
        using SimulationHost host = new(scene);

        float limit = (Strength / 60f) + 1e-4f;
        for (int step = 0; step < 60; step++)
        {
            host.Step();
            foreach (SpriteIntent intent in host.Simulation.View.Sprites.ToArray())
            {
                Vector2 move = Vector2.Abs(intent.Position - intent.PreviousPosition);
                Assert.True(move.X <= limit && move.Y <= limit, $"moved {move} in a step");
            }
        }

        Vector2 firstAt = Assert.Single(Drawn(host)).Position;
        Vector2 secondAt = Assert.Single(Drawn(host, size: 2f)).Position;
        Assert.True(Vector2.Distance(firstAt, new Vector2(8f, 8f)) > 1f);
        Assert.True(Vector2.Distance(firstAt, secondAt) > 1f);
    }

    private static Vector2 Push(Vector2 point)
    {
        Vector2 slope = ValueNoise.Slope(7, point);

        return new Vector2(slope.Y, -slope.X);
    }

    private static ParticleEmitter Swirling(Sprite sprite, float strength)
    {
        ParticleEmitter emitter = new(sprite, capacity: 1)
        {
            Lifetime = new FloatRange(10f, 10f),
            Noise = new ParticleNoise(strength, size: 16f),
        };
        emitter.Emit(1);

        return emitter;
    }
}
