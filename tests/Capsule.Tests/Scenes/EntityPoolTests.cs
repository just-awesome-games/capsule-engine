using System.Numerics;
using Capsule.Animation;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Drifter = Capsule.Tests.Scenes.SceneFixtures.Drifter;

namespace Capsule.Tests.Scenes;

public sealed class EntityPoolTests
{
    private static readonly SpriteClip Walk = new(
        [SceneFixtures.Frame(4, 4), SceneFixtures.Frame(4, 4)],
        [1, 1],
        loop: true);

    [Fact]
    public void ARemovedPooledEntity_IsIdleOnceTheRemovalLands()
    {
        EntityPool<Drifter> pool = new(() => new Drifter(), capacity: 1);
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);

        Drifter taken = pool.Take();
        scene.Add(taken);

        // Outside a step, the removal lands at once.
        scene.Remove(taken);
        Assert.Equal(1, pool.Available);

        // During a step, it lands at the drain: a Take in the same step builds a different entity.
        taken = pool.Take();
        scene.Add(taken);
        Drifter? takenMidStep = null;
        scene.Add(new SceneFixtures.Watcher(current =>
        {
            current.Remove(taken);
            takenMidStep = pool.Take();
        }));

        simulation.Step(SceneFixtures.Step());

        Assert.NotSame(taken, takenMidStep);
        Assert.Equal(2, pool.Capacity);
        Assert.Same(taken, pool.Take());
    }

    [Fact]
    public void SceneStop_ReturnsEveryPooledEntity()
    {
        EntityPool<Drifter> pool = new(() => new Drifter(), capacity: 1);
        Drifter taken = pool.Take();

        SceneFixtures.HookScene scene = new();
        using (SceneSimulation simulation = new(scene))
        {
            scene.Add(taken);
            Assert.Equal(0, pool.Available);
        }

        Assert.Same(taken, pool.Take());
    }

    [Fact]
    public void TakeOnAnEmptyPool_BuildsOneMore_TryTakeBuildsNothing()
    {
        EntityPool<Drifter> pool = new(() => new Drifter(), capacity: 1);
        pool.Take();

        Assert.NotNull(pool.Take());
        Assert.Equal(2, pool.Capacity);

        Assert.False(pool.TryTake(out _));
        Assert.Equal(2, pool.Capacity);
    }

    [Fact]
    public void AddingAnIdlePooledEntity_Throws()
    {
        // Never taken: the entity the constructor left idle, not one that has been through a life.
        Drifter? built = null;
        EntityPool<Drifter> pool = new(() => built = new Drifter(), capacity: 1);
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);

        Assert.Throws<InvalidOperationException>(() => scene.Add(built!));
        Assert.Throws<InvalidOperationException>(() => new EntityPool<Drifter>(() => built!, capacity: 1));
    }

    [Fact]
    public void APooledEntityRemovedFromUnderAParent_ReturnsAsARoot()
    {
        EntityPool<Drifter> pool = new(() => new Drifter(), capacity: 1);
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);

        Drifter holder = new();
        scene.Add(holder);
        Drifter leaf = pool.Take();
        leaf.Parent = holder;

        scene.Remove(leaf);

        Assert.Null(leaf.Parent);
        Assert.Equal(1, pool.Available);
    }

    [Fact]
    public void AJoin_CollapsesPreviousTransformOntoCurrent()
    {
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);

        // Positioned before its first add, then moved between a leave and a rejoin: neither smears.
        Sprited entity = new();
        entity.Position = new Vector2(-30f, 5f);
        scene.Add(entity);
        scene.Remove(entity);
        entity.Position = new Vector2(100f, 40f);
        scene.Add(entity);

        simulation.Step(SceneFixtures.Step());

        SpriteIntent frame = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(new Vector2(100f, 40f), frame.Position);
        Assert.Equal(frame.Position, frame.PreviousPosition);
    }

    [Fact]
    public void AReusedEntitysSpriteAnimatorAndParticleEmitter_ReadAsFresh()
    {
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);

        EntityPool<Animated> pool = new(() => new Animated(), capacity: 1);
        Animated first = pool.Take();
        scene.Add(first);

        first.Animator.Play(Walk);
        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step());

        Assert.True(first.Animator.FrameIndex > 0, "the walk never advanced past frame 0");
        Assert.True(first.Emitter.Alive > 0, "the emitter's prewarm never filled it");

        scene.Remove(first);

        Animated second = pool.Take();
        Assert.Same(first, second);
        Assert.Equal(0, second.Animator.FrameIndex);
        Assert.Same(Walk, second.Animator.Clip);
        Assert.Equal(0, second.Emitter.Alive);

        scene.Add(second);
        simulation.Step(SceneFixtures.Step());

        Assert.True(second.Emitter.Alive > 0, "the prewarm did not run again on the second life");
    }

    private sealed class Sprited : Entity
    {
        internal Sprited()
            : base(Vector2.Zero) =>
            Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
    }

    private sealed class Animated : Entity
    {
        internal Animated()
            : base(Vector2.Zero)
        {
            SpriteRenderer sprite = new(SceneFixtures.Frame(4, 4));
            Add(sprite);
            Animator = new SpriteAnimator(sprite);
            Add(Animator);

            Emitter = new ParticleEmitter(SceneFixtures.Frame(2, 2), capacity: 8)
            {
                PrewarmSeconds = 0.1f,
                Rate = 20f,
                Lifetime = (5f, 5f),
            };
            Add(Emitter);
        }

        internal SpriteAnimator Animator { get; }

        internal ParticleEmitter Emitter { get; }
    }
}
