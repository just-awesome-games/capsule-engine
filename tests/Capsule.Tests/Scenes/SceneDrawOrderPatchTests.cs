using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class SceneDrawOrderPatchTests
{
    private readonly List<Logged> _log = [];

    // A rebuild numbers the renderers from zero, which would hand the arrival the place of the one that left.
    [Fact]
    public void ARootArrivingAndOneLeaving_PatchTheOrderWithoutARebuild()
    {
        Node[] held = [Holding(2), Holding(0), Holding(1)];
        using SimulationHost run = Started(out Scene scene, held);

        scene.Remove(held[1]);
        Node arrival = Holding(1);
        scene.Add(arrival);
        run.Step();

        Assert.Equal([held[2].Get<Logged>(), arrival.Get<Logged>(), held[0].Get<Logged>()], Drawn(scene));
        Assert.Equal(3, arrival.DrawPosition);
    }

    [Fact]
    public void AnArrivalSettlingItsBandAndRenderersInItsHooks_DrawsAsTheyEndWithoutARebuild()
    {
        Node first = Holding(1);
        using SimulationHost run = Started(out Scene scene, first);

        Settling arrival = new(new Logged(_log));
        scene.Add(arrival);
        run.Step();

        Assert.Equal([arrival.Late, first.Get<Logged>()], Drawn(scene));
        Assert.Equal(1, arrival.DrawPosition);
    }

    // A rebuild numbers the wave from zero, past the one that left. What it leaves behind still patches.
    [Fact]
    public void AWaveTooLargeToInsert_RebuildsAndStillPatchesAfter()
    {
        Node first = Holding(1);
        using SimulationHost run = Started(out Scene scene, first);

        scene.Remove(first);
        Node[] wave = [.. Enumerable.Range(0, 1_000).Select(_ => Holding(0))];
        foreach (Node node in wave)
        {
            scene.Add(node);
        }

        run.Step();
        Assert.Equal(Expected(scene), Drawn(scene));
        Assert.Equal(0, wave[0].DrawPosition);

        scene.Remove(wave[500]);
        run.Step();
        Assert.Equal(Expected(scene), Drawn(scene));
    }

    [Fact]
    public void ARendererRemovingItsOwnerAsItAttaches_LeavesTheOrderDrawable()
    {
        Node stays = Holding(0);
        Node owner = new(Vector2.Zero);
        using SimulationHost run = Started(out Scene scene, stays, owner);

        owner.Add(new Logged(_log, attached: RemoveOwner));

        Assert.Null(owner.SceneOrNull);
        Assert.Equal([stays.Get<Logged>()], Drawn(scene));
    }

    [Fact]
    public void ARendererRemovingItsOwnerAsItDetaches_LeavesTheOrderDrawable()
    {
        Node stays = Holding(0);
        Node owner = new(Vector2.Zero);
        Logged sole = new(_log, detached: RemoveOwner);
        owner.Add(sole);
        using SimulationHost run = Started(out Scene scene, stays, owner);

        owner.Remove(sole);

        Assert.Null(owner.SceneOrNull);
        Assert.Equal([stays.Get<Logged>()], Drawn(scene));
    }

    // The read keys the renderer, and the rollback that follows takes it away again.
    [Fact]
    public void ARendererWhoseHookReadsTheOrderThenThrows_LeavesTheOrderDrawable()
    {
        Node owner = Holding(0);
        using SimulationHost run = Started(out Scene scene, owner);

        Logged refused = new(_log, attached: entity =>
        {
            Build(entity.SceneOrNull!);
            throw new InvalidOperationException("Refused.");
        });

        Assert.Throws<InvalidOperationException>(() => owner.Add(refused));
        Assert.Equal([owner.Get<Logged>()], Drawn(scene));
    }

    // The order accumulates patches across rounds, and each read is held to one worked out from the tree.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AnyMixOfChanges_LeavesTheOrderARebuildWouldGive(int seed)
    {
        const int Rounds = 200;

        Random random = new(seed);
        List<Node> removed = [];
        bool duringStep = false;

        void Hook(Scene scene, in StepContext context)
        {
            if (duringStep)
            {
                Change(scene, random, removed);
            }
        }

        SceneFixtures.HookScene scene = new(step: Hook);
        using SimulationHost run = new(scene);
        run.Step();

        for (int round = 0; round < Rounds; round++)
        {
            duringStep = random.Next(2) == 0;
            if (!duringStep)
            {
                Change(scene, random, removed);
            }

            run.Step();

            Assert.Equal(Expected(scene), Drawn(scene));
        }
    }

    private static void Build(Scene scene) => scene.Drawing.DrawFrame(new FrameView());

    private static void RemoveOwner(Entity owner) => owner.SceneOrNull?.Remove(owner);

    // A scene holding nodes that has stepped and read its order, which a change then patches.
    private static SimulationHost Started(out Scene scene, params Node[] nodes)
    {
        scene = new SceneFixtures.HookScene();
        foreach (Node node in nodes)
        {
            scene.Add(node);
        }

        SimulationHost run = new(scene);
        run.Step();
        Build(scene);

        return run;
    }

    private List<Logged> Drawn(Scene scene)
    {
        _log.Clear();
        Build(scene);

        return _log;
    }

    // Most rounds only spawn and despawn, which is what a patch serves. The rest mix in what rebuilds.
    private void Change(Scene scene, Random random, List<Node> removed)
    {
        bool spawnsOnly = random.Next(3) != 0;
        int changes = random.Next(1, 6);
        for (int change = 0; change < changes; change++)
        {
            Node? target = Held(scene, random);
            switch (random.Next(spawnsOnly ? 3 : 10))
            {
                case 0:
                case 1:
                    scene.Add(Spawned(random));
                    break;
                case 2 when target is not null:
                    scene.Remove(target);
                    removed.Add(target);
                    break;
                case 3 when target is not null:
                    new Node(Vector2.Zero) { Parent = target, ZIndexIsAbsolute = random.Next(2) == 0 }.Add(new Logged(_log) { ZIndex = random.Next(-2, 3) });
                    break;
                case 4 when target is not null && Held(scene, random) is { } parent && !IsUnder(parent, target):
                    target.Parent = parent;
                    break;
                case 5 when target is not null:
                    target.ZIndex = random.Next(-2, 3);
                    target.ZIndexIsAbsolute = random.Next(2) == 0;
                    break;
                case 6 when target is not null && target.TryGet(out Logged? renderer):
                    renderer.ZIndex = random.Next(-2, 3);
                    break;
                case 7 when target is not null:
                    target.Add(new Logged(_log) { ZIndex = random.Next(-2, 3) });
                    break;
                case 8 when target is not null && target.TryGet(out Logged? renderer):
                    target.Remove(renderer);
                    break;
                case 9:
                    TakeBack(scene, removed);
                    break;
            }
        }
    }

    private Node Spawned(Random random)
    {
        Node root = random.Next(4) == 0 ? new Node(Vector2.Zero) : Holding(random.Next(-2, 3));
        root.ZIndex = random.Next(-2, 3);
        if (random.Next(3) == 0)
        {
            new Node(Vector2.Zero) { Parent = root, ZIndex = random.Next(-2, 3), ZIndexIsAbsolute = random.Next(2) == 0 }.Add(new Logged(_log));
        }

        root.Add(new Mover());

        return root;
    }

    private static void TakeBack(Scene scene, List<Node> removed)
    {
        Node? back = removed.FindLast(static node => node.SceneOrNull is null && node.PendingScene is null && node.Parent is null);
        if (back is not null)
        {
            removed.Remove(back);
            scene.Add(back);
        }
    }

    private static Node? Held(Scene scene, Random random)
    {
        Node[] held = [.. scene.Entities.ToArray().OfType<Node>().Where(scene.Contains)];

        return held.Length == 0 ? null : held[random.Next(held.Length)];
    }

    private static bool IsUnder(Entity entity, Entity above)
    {
        for (Entity? at = entity; at is not null; at = at.Parent)
        {
            if (ReferenceEquals(at, above))
            {
                return true;
            }
        }

        return false;
    }

    private static Logged[] Expected(Scene scene) =>
    [
        .. scene.Entities.ToArray()
            .SelectMany(static entity => entity.Components.ToArray().OfType<Logged>())
            .OrderBy(static renderer => Band(renderer.Entity!) + renderer.ZIndex),
    ];

    private static long Band(Entity entity)
    {
        long band = 0;
        for (Entity? at = entity; at is not null; at = at.ZIndexIsAbsolute ? null : at.Parent)
        {
            band += at.ZIndex;
        }

        return band;
    }

    private Node Holding(int zIndex)
    {
        Node node = new(Vector2.Zero);
        node.Add(new Logged(_log) { ZIndex = zIndex });

        return node;
    }

    private sealed class Logged(List<Logged> log, Action<Entity>? attached = null, Action<Entity>? detached = null) : Renderer
    {
        protected internal override void Draw(FrameView view) => log.Add(this);

        protected internal override void OnAttached(Entity entity) => attached?.Invoke(entity);

        protected internal override void OnDetached(Entity entity) => detached?.Invoke(entity);
    }

    // Takes its band and its renderer only as it joins.
    private sealed class Settling(Logged late) : Entity(Vector2.Zero)
    {
        internal Logged Late => late;

        protected internal override void OnAddedToScene()
        {
            ZIndex = -1;
            Add(late);
        }
    }
}
