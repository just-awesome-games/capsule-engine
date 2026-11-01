using System.Numerics;
using Capsule.Animation;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Particles;

public sealed class ParticleBoundsTests
{
    [Fact]
    public void Bounds_CoversEveryLiveParticleAndReadsEmptyWithNoneAlive()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = ParticleEmitterTests.Build(capacity: 8);

        Assert.True(emitter.Bounds.IsEmpty);

        emitter.Emit(1);
        host.Step();

        Assert.False(emitter.Bounds.IsEmpty);

        SpriteIntent intent = host.Simulation.View.Sprites.ToArray().Single();
        Rect bounds = emitter.Bounds;
        Assert.True(bounds.Contains(intent.Position) || intent.Position.X == bounds.Right || intent.Position.Y == bounds.Bottom);

        // A spawn after the step that folded Bounds is not lost until the next fold: Emit extends
        // Bounds itself. A late burst under a culling camera still draws the same frame.
        Vector2 far = new(100f, 100f);
        emitter.Emit(1, far);

        Assert.True(emitter.Bounds.Contains(far) || far.X == emitter.Bounds.Right || far.Y == emitter.Bounds.Bottom);
    }

    // A frame anchored at its corner reaches its whole diagonal from the particle. Bounds once grew by half of it, as
    // if every frame were anchored at its centre.
    [Fact]
    public void Bounds_CoverTheFarCornerOfAFrameAnchoredAtItsCorner()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = ParticleEmitterTests.Build(capacity: 8);
        emitter.Emit(1);
        host.Step();

        Assert.True(emitter.Bounds.Contains(new Vector2(3.9f, 3.9f)));
    }

    // A negative scale mirrors the frame. Its bounds once inflated by a negative reach and read empty,
    // which culled the particle.
    [Fact]
    public void Bounds_InflateByTheScalesMagnitude()
    {
        (_, ParticleEmitter upright, _) = ParticleEmitterTests.Build(capacity: 8);
        upright.Scale = 2f;
        upright.Emit(1);

        (_, ParticleEmitter mirrored, _) = ParticleEmitterTests.Build(capacity: 8);
        mirrored.Scale = -2f;
        mirrored.Emit(1);

        Assert.Equal(upright.Bounds, mirrored.Bounds);
    }

    // A particle keeps the scale it spawned at. Bounds once grew by the emitter's current Scale, and a smaller Scale set
    // after a large particle spawned left that particle's frame outside them.
    [Fact]
    public void Bounds_CoverALiveParticleSpawnedAtALargerScale()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = ParticleEmitterTests.Build(capacity: 8);
        emitter.Scale = 10f;
        emitter.Emit(1);
        host.Step();

        emitter.Scale = 1f;

        Assert.True(emitter.Bounds.Contains(new Vector2(39f, 39f)));
    }

    // An emitter wider than the view once handed the view every particle to cull, which cost a wide emitter its whole
    // pool each frame. The particle left of the view reaches into it by its frame's far corner and still draws.
    [Fact]
    public void AnEmitterWiderThanTheView_SubmitsOnlyTheParticlesThatReachIt()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = ParticleEmitterTests.Build(capacity: 4);
        emitter.Lifetime = new FloatRange(5f, 5f);
        foreach (float x in (float[])[-40f, -23f, 0f, 30f])
        {
            emitter.Emit(1, new Vector2(x, 0f));
        }

        host.Step();
        FrameView view = new() { Camera = new CameraView(Vector2.Zero, new Vector2(40f, 40f)) };
        emitter.Draw(view);

        Assert.Equal(new RenderMetrics(Submitted: 2, Visible: 2), view.Metrics);
    }

    // A turned frame's reach is a square root, which the view's own test and the emitter's round apart. A frame reaching
    // the region's edge by that rounding alone is still drawn.
    [Fact]
    public void AParticleReachingTheViewByRoundingAlone_StillDraws()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = ParticleEmitterTests.Build(capacity: 2, SceneFixtures.Frame(1, 1));
        emitter.Scale = 0.9f;
        emitter.Rotation = 30f;
        emitter.Lifetime = new FloatRange(5f, 5f);
        emitter.Emit(1, new Vector2(-1.2727921f, 5f));
        emitter.Emit(1, new Vector2(10f, 5f));

        host.Step();
        FrameView view = new() { Camera = new CameraView(new Vector2(10f, 0f), new Vector2(20f, 20f)) };
        emitter.Draw(view);

        Assert.Equal(new RenderMetrics(Submitted: 2, Visible: 2), view.Metrics);
    }

    // The emitter's own cull must never drop a sprite the view would keep. Its reach is worked out apart from the sprite
    // it builds. The levers that size or place a frame are drawn at random here, and the view keeps exactly what it
    // would keep from the whole unculled pool.
    [Fact]
    public void TheEmittersCull_KeepsEverySpriteTheViewWouldKeep()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            Random random = new(seed);
            (FrameView view, ParticleEmitter emitter) = RandomRun(random);

            FrameView whole = new();
            emitter.Draw(whole);
            FrameView expected = new() { Camera = view.Camera };
            foreach (SpriteIntent intent in whole.Sprites)
            {
                expected.Add(in intent);
            }

            emitter.Draw(view);

            // An emitter whose bounds sit inside the region hands the view its sprites unculled. The kept sprites are tested
            // again as the view tests them.
            FrameView kept = new() { Camera = view.Camera };
            foreach (SpriteIntent intent in view.Sprites)
            {
                kept.Add(in intent);
            }

            Assert.True(expected.Sprites.SequenceEqual(kept.Sprites), $"seed {seed}");
        }
    }

    // An emitter with random frames, pivots, flips, turns, scales and space under a random parent that moves and turns
    // between steps, stepped a few times with a Scale change after the first spawn and sometimes held, and a camera over
    // part of its particles.
    private static (FrameView View, ParticleEmitter Emitter) RandomRun(Random random)
    {
        float Between(float min, float max) => min + (random.NextSingle() * (max - min));
        FloatRange Range(float min, float max)
        {
            float first = Between(min, max);
            float second = Between(min, max);

            return new FloatRange(MathF.Min(first, second), MathF.Max(first, second));
        }

        Sprite[] frames = new Sprite[random.Next(1, 4)];
        for (int index = 0; index < frames.Length; index++)
        {
            int width = random.Next(1, 17);
            int height = random.Next(1, 17);
            frames[index] = new Sprite(
                SceneFixtures.Atlas,
                new TextureRegion(0, 0, width, height),
                new Vector2(Between(-8f, width + 8f), Between(-8f, height + 8f)));
        }

        ParticleEmitter emitter = new(new SpriteClip(frames, [.. frames.Select(_ => 1)], loop: true), capacity: 32)
        {
            Space = random.Next(2) == 0 ? ParticleSpace.World : ParticleSpace.Local,
            FrameMode = (FrameMode)random.Next(4),
            FlipX = random.Next(2) == 0,
            FlipY = random.Next(2) == 0,
            Rotation = random.Next(2) == 0 ? 0f : Range(-180f, 180f),
            AngularVelocity = random.Next(2) == 0 ? 0f : Between(-720f, 720f),
            Scale = Range(-3f, 3f),
            ScaleOverLifetime = Curve.Linear(Between(0f, 2f), Between(0f, 2f)),
            Speed = (0f, Between(0f, 6000f)),
            Spread = 360f,
            Lifetime = 5f,
        };
        (EntityHierarchyFixtures.Node root, _, SimulationHost host) = ParticleEmitterTests.Build(emitter);
        root.Position = new Vector2(Between(-40f, 40f), Between(-40f, 40f));
        root.Rotation = random.Next(2) == 0 ? 0f : Between(-3.2f, 3.2f);
        root.Scale = new Vector2(Between(-2f, 2f), Between(-2f, 2f));

        Vector2 center = new(Between(-30f, 30f), Between(-30f, 30f));
        Vector2 size = new(Between(10f, 80f), Between(10f, 80f));

        // Half the particles start against the view's edges, where a reach that falls short shows first.
        for (int index = 0; index < 24; index++)
        {
            Vector2 at = index % 2 == 0
                ? new Vector2(Between(-90f, 90f), Between(-90f, 90f))
                : center + new Vector2(
                    (random.Next(2) == 0 ? -1f : 1f) * ((size.X / 2f) + Between(-20f, 20f)),
                    Between(-size.Y, size.Y));
            emitter.Emit(1, at);
            if (index == 11)
            {
                host.Step();
                emitter.Scale = Between(0f, 0.5f);
            }
        }

        for (int step = random.Next(1, 4); step > 0; step--)
        {
            root.Position += new Vector2(Between(-30f, 30f), Between(-30f, 30f));
            root.Rotation += Between(-1f, 1f);
            host.Step();
        }

        if (random.Next(3) == 0)
        {
            root.Scene.Freeze(2);
            host.Step();
        }

        return (new FrameView { Camera = new CameraView(center, size) }, emitter);
    }
}
