using System.Numerics;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Particles;

// The style of tests/Capsule.Tests/Runtime/HeadlessDeterminismTests.cs: a scene built in code, stepped
// through a host, and read back from the frame it drew. Contracts, one canonical test each.
public sealed class ParticleEmitterTests
{
    private static readonly Sprite Tile = SceneFixtures.Frame(4, 4);

    [Fact]
    public void TwoHeadlessRuns_EmitIdenticalIntentsOnTheSameTick()
    {
        SpriteIntent[] first = Play(seed: 7);
        SpriteIntent[] second = Play(seed: 7);

        Assert.Equal(first, second);
    }

    // Two long-lived particles spawned first, a short-lived one spawned last: ageing brings the
    // short-lived one closer to the end of its life sooner, even though it is the youngest by spawn
    // order, and a fourth spawn recycles it rather than the chronologically oldest slot.
    [Fact]
    public void EmitBeyondCapacity_RecyclesTheParticleNearestTheEndOfItsLife()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = Build(capacity: 3);

        emitter.Lifetime = new FloatRange(5f, 5f);
        emitter.Emit(1, new Vector2(1f, 0f));
        emitter.Emit(1, new Vector2(2f, 0f));

        emitter.Lifetime = new FloatRange(0.1f, 0.1f);
        emitter.Emit(1, new Vector2(3f, 0f));

        Assert.Equal(3, emitter.Alive);

        // Five ticks: the short-lived particle (6 ticks) is most of the way through its life, the two
        // long-lived ones (300 ticks) barely into theirs, and none has expired yet.
        host.Step(5);
        Assert.Equal(3, emitter.Alive);

        emitter.Emit(1, new Vector2(4f, 0f));
        Assert.Equal(3, emitter.Alive);

        // Redraws the frame, with no rate spawning to disturb the slots further.
        host.Step();

        float[] xPositions = [.. host.Simulation.View.Sprites.ToArray().Select(static intent => intent.Position.X)];
        Assert.Equal([1f, 2f, 4f], xPositions);
    }

    [Fact]
    public void Rate_AccumulatesTheFractionAcrossTicks()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = Build(capacity: 16);
        emitter.Rate = 7f;
        emitter.Lifetime = new FloatRange(2f, 2f);

        host.Step(60);

        Assert.Equal(7, emitter.Alive);
    }

    [Fact]
    public void SubTickSpawn_PlacesOriginsAcrossTheTicksDisplacement()
    {
        // Zero speed: Position and PreviousPosition both land exactly on the origin, so the fraction
        // is read straight off the drawn position.
        Mover stillMover = new(Vector2.Zero);
        ParticleEmitter stillEmitter = new(Tile, capacity: 8) { Rate = 240f, Lifetime = new FloatRange(2f, 2f) };
        stillMover.Add(stillEmitter);

        SceneFixtures.HookScene stillScene = new(start: s => s.Add(stillMover));
        SimulationHost stillHost = new(stillScene);
        stillHost.Step();

        float[] fractions = [.. stillHost.Simulation.View.Sprites.ToArray().Select(static intent => intent.Position.X)];
        Assert.Equal(4, fractions.Length);
        AssertClose([0.125f, 0.375f, 0.625f, 0.875f], fractions);

        // A drawn speed: whatever fraction a spawn lands at, Position - PreviousPosition is exactly
        // Velocity * dt, by the same placement the still case used.
        Mover movingMover = new(Vector2.Zero);
        ParticleEmitter movingEmitter = new(Tile, capacity: 8)
        {
            Rate = 240f,
            Speed = new FloatRange(50f, 50f),
            Lifetime = new FloatRange(2f, 2f),
        };
        movingMover.Add(movingEmitter);

        SceneFixtures.HookScene movingScene = new(start: s => s.Add(movingMover));
        SimulationHost movingHost = new(movingScene);
        movingHost.Step();

        float dt = 1f / 60f;
        foreach (SpriteIntent intent in movingHost.Simulation.View.Sprites.ToArray())
        {
            Vector2 delta = intent.Position - intent.PreviousPosition;
            Assert.Equal(new Vector2(50f, 0f) * dt, delta);
        }
    }

    // The burst-on-spawn case: Emit called from the entity's own constructor, before the emitter has
    // a random source or a transform. The pending spawn waits for OnStart.
    [Fact]
    public void EmitBeforeTheEmitterStarts_SpawnsInOnStart()
    {
        BurstOnConstruction entity = null!;

        SceneFixtures.HookScene scene = new(
            step: (Scene s, in StepContext context) =>
            {
                if (context.Tick == 0)
                {
                    entity = new BurstOnConstruction(Vector2.Zero);
                    s.Add(entity);
                }
            });

        SimulationHost host = new(scene);
        host.Step();

        Assert.Equal(6, entity.Emitter.Alive);
    }

    [Fact]
    public void EmitAfterTheRequestingEntitysRemoval_StillDrawsOnTheNextFrame()
    {
        ParticleEmitter shared = new(Tile, capacity: 8) { Lifetime = new FloatRange(2f, 2f) };
        Root pool = new(Vector2.Zero);
        pool.Add(shared);

        SceneFixtures.HookScene scene = new(start: s =>
        {
            s.Add(pool);
            s.Add(new Requester(shared));
        });

        SimulationHost host = new(scene);
        host.Step();
        host.Step();

        Assert.True(shared.Alive > 0);
        Assert.False(host.Simulation.View.Sprites.IsEmpty);
    }

    [Fact]
    public void PrewarmSeconds_FillsThePoolOnTheFirstFrame()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = Build(capacity: 10);
        emitter.Rate = 1000f;
        emitter.Lifetime = new FloatRange(5f, 5f);
        emitter.PrewarmSeconds = 1f;

        host.Step();

        Assert.Equal(10, emitter.Alive);
    }

    [Fact]
    public void TheEmitter_DrawsNothingFromRunRandom()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = Build(capacity: 16);
        emitter.Rate = 20f;
        emitter.Shape = EmitShape.Circle(4f);
        emitter.Speed = new FloatRange(1f, 5f);

        ulong before = host.Run.Random.DrawCount;
        host.Step(3);

        Assert.Equal(before, host.Run.Random.DrawCount);
    }

    [Fact]
    public void Bounds_CoversEveryLiveParticleAndReadsEmptyWithNoneAlive()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = Build(capacity: 8);

        Assert.True(emitter.Bounds.IsEmpty);

        emitter.Emit(1);
        host.Step();

        Assert.False(emitter.Bounds.IsEmpty);

        SpriteIntent intent = host.Simulation.View.Sprites.ToArray().Single();
        Rect bounds = emitter.Bounds;
        Assert.True(bounds.Contains(intent.Position) || intent.Position.X == bounds.Right || intent.Position.Y == bounds.Bottom);

        // A spawn after the step that folded Bounds is not lost until the next fold: Emit extends
        // Bounds itself, so a late burst under a culling camera still draws the same frame.
        Vector2 far = new(100f, 100f);
        emitter.Emit(1, far);

        Assert.True(emitter.Bounds.Contains(far) || far.X == emitter.Bounds.Right || far.Y == emitter.Bounds.Bottom);
    }

    private static void AssertClose(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.True(MathF.Abs(expected[index] - actual[index]) <= 1e-4f, $"expected {expected[index]}, got {actual[index]}");
        }
    }

    private static SpriteIntent[] Play(ulong seed)
    {
        ParticleEmitter burst = null!;
        ParticleEmitter continuous = null!;

        SceneFixtures.HookScene scene = new(
            start: s =>
            {
                Root root = new(Vector2.Zero);
                burst = new ParticleEmitter(Tile, capacity: 8) { Lifetime = new FloatRange(2f, 2f) };
                continuous = new ParticleEmitter(Tile, capacity: 8) { Rate = 5f, Lifetime = new FloatRange(2f, 2f) };
                root.Add(burst);
                root.Add(continuous);
                s.Add(root);
            },
            step: (Scene _, in StepContext context) =>
            {
                if (context.Tick == 0)
                {
                    burst.Emit(3);
                }
            });

        SimulationHost host = new(scene, run: new Run(new RandomSource(seed)));
        host.Step(10);

        return [.. host.Simulation.View.Sprites.ToArray()];
    }

    private static (Root Root, ParticleEmitter Emitter, SimulationHost Host) Build(int capacity)
    {
        ParticleEmitter emitter = new(Tile, capacity);
        Root root = new(Vector2.Zero);
        root.Add(emitter);

        SceneFixtures.HookScene scene = new(start: s => s.Add(root));
        SimulationHost host = new(scene);

        return (root, emitter, host);
    }

    private sealed class Root(Vector2 position) : Entity(position);

    // Moves one world unit on X every tick, so a spawn's sub-tick fraction is directly readable off
    // its origin.
    private sealed class Mover(Vector2 position) : Entity(position)
    {
        protected internal override void OnStep(in StepContext context) => Position += new Vector2(1f, 0f);
    }

    // Emits into a pool it does not own, then leaves the scene the same step, as Bolt does onto its
    // SparkBurst.
    private sealed class Requester(ParticleEmitter pool) : Entity(Vector2.Zero)
    {
        protected internal override void OnStep(in StepContext context)
        {
            pool.Emit(2, Position);
            Scene.Remove(this);
        }
    }

    // Adds an emitter and emits from its own constructor, before either is in a scene: the
    // burst-on-spawn pattern SparkBurst uses.
    private sealed class BurstOnConstruction : Entity
    {
        internal ParticleEmitter Emitter { get; }

        internal BurstOnConstruction(Vector2 position)
            : base(position)
        {
            Emitter = new ParticleEmitter(Tile, capacity: 8) { Lifetime = new FloatRange(2f, 2f) };
            Add(Emitter);
            Emitter.Emit(6);
        }
    }
}
