using System.Numerics;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Particles;

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
        // Zero speed: Position and PreviousPosition both land exactly on the origin. The fraction
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

    // Early calls at different points, as a burst from several places queues before its entity joins.
    [Fact]
    public void EmitsBeforeTheEmitterStarts_EachSpawnAtItsOwnPoint()
    {
        ParticleEmitter emitter = new(Tile, capacity: 8) { Lifetime = new FloatRange(2f, 2f) };
        EntityHierarchyFixtures.Node root = new(Vector2.Zero);
        root.Add(emitter);

        SceneFixtures.HookScene scene = new(
            step: (Scene s, in StepContext context) =>
            {
                if (context.Tick == 0)
                {
                    emitter.Emit(1, new Vector2(-10f, 0f));
                    emitter.Emit(2, new Vector2(10f, 0f));
                    s.Add(root);
                }
            });

        SimulationHost host = new(scene);
        host.Step(2);

        float[] xPositions = [.. host.Simulation.View.Sprites.ToArray().Select(static intent => intent.Position.X)];
        Assert.Equal([-10f, 10f, 10f], xPositions);
    }

    [Fact]
    public void ClearBeforeTheEmitterStarts_CancelsTheWaitingEmit()
    {
        BurstOnConstruction entity = null!;

        SceneFixtures.HookScene scene = new(
            step: (Scene s, in StepContext context) =>
            {
                if (context.Tick == 0)
                {
                    entity = new BurstOnConstruction(Vector2.Zero);
                    entity.Emitter.Clear();
                    s.Add(entity);
                }
            });

        SimulationHost host = new(scene);
        host.Step();

        Assert.Equal(0, entity.Emitter.Alive);
    }

    [Fact]
    public void EmitAfterTheRequestingEntitysRemoval_StillDrawsOnTheNextFrame()
    {
        ParticleEmitter shared = new(Tile, capacity: 8) { Lifetime = new FloatRange(2f, 2f) };
        EntityHierarchyFixtures.Node pool = new(Vector2.Zero);
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
    public void AOneTickParticleEmittedFromTheEntitysOwnStep_DrawsOnce()
    {
        SelfEmitter entity = new();
        SimulationHost host = new(new SceneFixtures.HookScene(start: s => s.Add(entity)));

        host.Step();
        Assert.Single(host.Simulation.View.Sprites.ToArray());

        host.Step();
        Assert.Empty(host.Simulation.View.Sprites.ToArray());
    }

    [Theory]
    [MemberData(nameof(BadSettings))]
    public void ASetting_RejectsAValueOutsideItsRange(Action<ParticleEmitter> set)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => set(new ParticleEmitter(Tile, capacity: 1)));
    }

    public static IEnumerable<object[]> BadSettings()
    {
        yield return [new Action<ParticleEmitter>(e => e.Rate = -1f)];
        yield return [new Action<ParticleEmitter>(e => e.RateOverDistance = float.NaN)];
        yield return [new Action<ParticleEmitter>(e => e.Spread = 361f)];
        yield return [new Action<ParticleEmitter>(e => e.Damping = -1f)];
        yield return [new Action<ParticleEmitter>(e => e.PrewarmSeconds = float.PositiveInfinity)];
        yield return [new Action<ParticleEmitter>(e => e.InheritVelocity = float.NaN)];
        yield return [new Action<ParticleEmitter>(e => e.Shape = EmitShape.Circle(-1f))];
        yield return [new Action<ParticleEmitter>(e => e.Shape = EmitShape.Ring(float.NaN))];
        yield return [new Action<ParticleEmitter>(e => e.Shape = EmitShape.Rect(1f, -1f))];
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
                EntityHierarchyFixtures.Node root = new(Vector2.Zero);
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

    internal static (EntityHierarchyFixtures.Node Root, ParticleEmitter Emitter, SimulationHost Host) Build(int capacity, Sprite? frame = null) =>
        Build(new ParticleEmitter(frame ?? Tile, capacity));

    // The emitter on a root entity at the origin, in a scene of its own.
    internal static (EntityHierarchyFixtures.Node Root, ParticleEmitter Emitter, SimulationHost Host) Build(ParticleEmitter emitter)
    {
        EntityHierarchyFixtures.Node root = new(Vector2.Zero);
        root.Add(emitter);

        SceneFixtures.HookScene scene = new(start: s => s.Add(root));
        SimulationHost host = new(scene);

        return (root, emitter, host);
    }

    // Moves one world unit on X every tick. A spawn's sub-tick fraction is directly readable off
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

    // Emits one particle that lives one step from its own OnStep, which runs before its emitter's step.
    private sealed class SelfEmitter : Entity
    {
        private readonly ParticleEmitter _emitter = new(Tile, capacity: 1) { Lifetime = new FloatRange(0.001f, 0.001f) };
        private bool _emitted;

        internal SelfEmitter()
            : base(Vector2.Zero) =>
            Add(_emitter);

        protected internal override void OnStep(in StepContext context)
        {
            if (!_emitted)
            {
                _emitted = true;
                _emitter.Emit(1);
            }
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
