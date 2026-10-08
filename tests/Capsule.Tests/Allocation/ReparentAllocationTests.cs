using System.Numerics;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class ReparentAllocationTests
{
    private const int Period = 60;

    // The first step builds the scene's first frame. The first reparent lands well after it.
    private const int WarmupSteps = 3;

    // The first scene runs every code path. The second starts with empty collections and is measured from its fourth step.
    [Fact]
    public void AReparentEverySecond_AllocatesNothing_FromTheFirstOne()
    {
        _ = Run(new Launcher());
        Launcher launcher = new();
        long bytes = Run(launcher);

        Assert.True(launcher.Launches >= 10);
        Assert.Equal(0, bytes);
    }

    // More leaves and returns than the root list has room for dead entries, all inside one step.
    [Fact]
    public void ALeaveAndReturnBurstInOneStep_AllocatesNothing()
    {
        _ = RunBurst();

        Assert.Equal(0, RunBurst());
    }

    private static long RunBurst()
    {
        Entity parent = new EntityHierarchyFixtures.Node(Vector2.Zero);
        Entity traveller = new EntityHierarchyFixtures.Node(Vector2.Zero);
        int steps = 0;
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, new Vector2(1000f, 1000f)));
        scene.Add(parent);
        scene.Add(traveller);
        scene.Add(new EntityHierarchyFixtures.Node(Vector2.Zero));
        scene.Add(new SceneFixtures.Watcher(_ =>
        {
            // The warmup grows the parent's child list once.
            int cycles = ++steps <= WarmupSteps ? 1 : 16;
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                traveller.Parent = parent;
                traveller.Parent = null;
            }
        }));
        using SimulationHost host = new(scene);
        long before = 0;

        for (int index = 0; index < Period; index++)
        {
            if (index == WarmupSteps)
            {
                before = GC.GetAllocatedBytesForCurrentThread();
            }

            host.Step();
            _ = host.Simulation.View;
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static long Run(Launcher launcher)
    {
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, new Vector2(1000f, 1000f)));
        scene.Add(launcher);
        scene.Add(new SceneFixtures.Watcher(_ => launcher.Cycle()));
        using SimulationHost host = new(scene);
        long before = 0;

        for (int index = 0; index < Period * 10; index++)
        {
            if (index == WarmupSteps)
            {
                before = GC.GetAllocatedBytesForCurrentThread();
            }

            host.Step();
            _ = host.Simulation.View;
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class Shot : Entity
    {
        internal Shot()
            : base(Vector2.Zero)
        {
            Add(new BoxCollider2D(new Vector2(4f, 4f)));
            Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
        }
    }

    private sealed class Launcher() : Entity(new Vector2(50f, 50f))
    {
        private readonly EntityPool<Shot> _shots = new(static () => new Shot(), capacity: 1);
        private Shot? _shot;
        private int _steps;

        internal int Launches { get; private set; }

        protected internal override void CollectAssets(AssetCollection assets) => _shots.CollectAssets(assets);

        internal void Cycle()
        {
            _steps++;
            if (_shot is null)
            {
                _shot = _shots.Take();
                _shot.Parent = this;
                return;
            }

            if (_steps % Period == 0)
            {
                _shot.Parent = null;
                Launches++;
            }
            else if (_steps % Period == Period / 2)
            {
                _shot.Parent = this;
            }
        }
    }
}
