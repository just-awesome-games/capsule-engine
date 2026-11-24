using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;
using Recorder = Capsule.Tests.Scenes.SceneFixtures.Recorder;
using Watcher = Capsule.Tests.Scenes.SceneFixtures.Watcher;

namespace Capsule.Tests.Scenes;

public sealed class EntityTreeOrderTests
{
    [Fact]
    public void ARootThatLeavesAndReturnsRepeatedly_ReturnsToTheEndOfTheRoots()
    {
        Scene scene = new();
        Node first = new(Vector2.Zero);
        Node traveller = new(Vector2.Zero);
        Node last = new(Vector2.Zero);
        scene.Add(first);
        scene.Add(traveller);
        scene.Add(last);

        for (int cycle = 0; cycle < 20; cycle++)
        {
            traveller.Parent = first;
            traveller.Parent = null;
        }

        Assert.Equal<Entity>([first, last, traveller], scene.Entities.ToArray());
    }

    [Fact]
    public void AfterInterleavedInsertsVacatesAndReparents_ChildrenFollowTheirParentAndRootsKeepTheirJoinOrder()
    {
        Scene scene = new();
        Node[] early = [.. Enumerable.Range(0, 12).Select(_ => new Node(Vector2.Zero))];
        foreach (Node node in early)
        {
            scene.Add(node);
        }

        for (int index = 1; index < early.Length; index += 2)
        {
            scene.Remove(early[index]);
        }

        Node[] late = [.. Enumerable.Range(0, 6).Select(_ => new Node(Vector2.Zero))];
        foreach (Node node in late)
        {
            scene.Add(node);
        }

        Node child = new(Vector2.Zero) { Parent = early[0] };
        late[1].Parent = early[0];
        early[2].Parent = early[4];
        early[2].Parent = null;
        late[3].Parent = early[4];

        Entity[] expected =
        [
            early[0], child, late[1], early[4], late[3], early[6], early[8], early[10],
            late[0], late[2], late[4], late[5], early[2],
        ];
        Assert.Equal(expected, scene.Entities.ToArray());
    }

    // Children joining held parents in one drain land in the middle of the tree. The drain places each
    // where it belongs, and starts them parent-first in tree order.
    [Fact]
    public void ChildrenJoiningHeldParentsInOneStep_LandInTreeOrderAndStartInIt()
    {
        List<string> log = [];
        Scene scene = new();
        Node first = new(Vector2.Zero);
        Node second = new(Vector2.Zero);
        Node third = new(Vector2.Zero);
        Recorder elder = new("elder", log, logsStart: true);
        Recorder grandchild = new("grandchild", log, logsStart: true) { Parent = elder };
        Recorder younger = new("younger", log, logsStart: true);
        Recorder middle = new("middle", log, logsStart: true);
        Recorder last = new("last", log, logsStart: true);
        int steps = 0;
        Watcher watcher = new(_ =>
        {
            if (++steps == 2)
            {
                last.Parent = third;
                elder.Parent = first;
                middle.Parent = second;
                younger.Parent = first;
            }
        });
        scene.Add(first);
        scene.Add(second);
        scene.Add(third);
        scene.Add(watcher);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step(tick: 1));

        Assert.Equal<Entity>([first, elder, grandchild, younger, second, middle, third, last, watcher], scene.Entities.ToArray());
        Assert.Equal(["elder!", "grandchild!", "younger!", "middle!", "last!"], log.Where(static entry => entry.EndsWith('!')));
    }

    // A join hook in that drain reads the scene in tree order, as it would if each child had been
    // inserted in place.
    [Fact]
    public void AJoinHookInADrainOfMidListChildren_ReadsTheSceneInTreeOrder()
    {
        Scene scene = new();
        Node parent = new(Vector2.Zero);
        Node other = new(Vector2.Zero);
        Node first = new(Vector2.Zero);
        Observer second = new();
        int steps = 0;
        Watcher watcher = new(_ =>
        {
            if (++steps == 2)
            {
                first.Parent = parent;
                second.Parent = parent;
            }
        });
        scene.Add(parent);
        scene.Add(other);
        scene.Add(watcher);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step(tick: 1));

        Assert.Equal<Entity>([parent, first, second, other, watcher], second.Seen);
    }

    // Children join, or roots are written under a parent, one after another ahead of the same root until
    // no label is left between two of them. The tree spreads its labels again, and each key the draw order
    // holds follows its entity.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildrenArrivingUntilTheLabelsBetweenTwoRootsRunOut_KeepTreeOrderAndPatchTheDrawOrder(bool reparented)
    {
        List<Entity> drawn = [];
        Scene scene = new();
        Node first = new(Vector2.Zero);
        Node last = new(Vector2.Zero);
        first.Add(new Logging(drawn));
        last.Add(new Logging(drawn));
        scene.Add(first);
        scene.Add(last);
        using SimulationHost run = new(scene);
        run.Step();
        FrameView view = new();
        scene.Drawing.DrawFrame(view);
        int rebuilds = scene.Drawing.Rebuilds;

        List<Entity> expected = [first];
        for (int index = 0; index < 70_000; index++)
        {
            Node child = new(Vector2.Zero) { Parent = reparented ? null : first };
            child.Add(new Logging(drawn));
            if (reparented)
            {
                scene.Add(child);
                child.Parent = first;
            }

            expected.Add(child);
            if (index % 10_000 == 0)
            {
                scene.Drawing.DrawFrame(view);
            }
        }

        expected.Add(last);
        drawn.Clear();
        scene.Drawing.DrawFrame(view);

        Assert.Equal(expected, scene.Entities.ToArray());
        Assert.Equal(expected, drawn);
        Assert.Equal(rebuilds, scene.Drawing.Rebuilds);
    }

    // A parent write moves only its subtree. After any run of writes between steps or during one, under a
    // pause now and then, over entities of every StepMode with bands that sum and bands that are absolute,
    // the scene holds its entities in tree order and draws them by band, patching an order it built once.
    // It steps them as a scene that resolves every hold again before each step does.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AnyRunOfParentWrites_KeepsTreeAndDrawOrder_AndStepsAsResolvingEveryHoldWould(int seed) =>
        Assert.Equal(WriteParents(seed, resolveAll: true), WriteParents(seed, resolveAll: false));

    // The step log of 40 rounds of random parent writes.
    private static List<string> WriteParents(int seed, bool resolveAll)
    {
        Random random = new(seed);
        List<string> log = [];
        Recorder[] held = new Recorder[12];
        int[] tagZ = new int[held.Length];
        for (int index = 0; index < held.Length; index++)
        {
            held[index] = new($"e{index}", log)
            {
                ZIndex = random.Next(-2, 3),
                ZIndexIsAbsolute = random.Next(3) == 0,
                StepMode = (StepMode)random.Next(6),
                Parent = index == 0 || random.Next(3) == 0 ? null : held[random.Next(index)],
            };
            tagZ[index] = random.Next(-1, 2);
            held[index].Add(Tag(index, tagZ[index]));
        }

        // The roots in the order they joined the end.
        bool duringStep = false;
        List<Entity> roots = [];
        void Write(bool now)
        {
            for (int write = now ? random.Next(1, 4) : 0; write > 0; write--)
            {
                Recorder target = held[random.Next(held.Length)];
                Entity? parent = random.Next(3) == 0 ? null : held[random.Next(held.Length)];
                Entity? above = parent;
                while (above is not null && !ReferenceEquals(above, target))
                {
                    above = above.Parent;
                }

                if (above is null)
                {
                    if (target.Parent is null && parent is not null)
                    {
                        roots.Remove(target);
                    }
                    else if (target.Parent is not null && parent is null)
                    {
                        roots.Add(target);
                    }

                    target.Parent = parent;
                }
            }
        }

        roots.Add(new Watcher(_ => Write(duringStep)) { StepMode = StepMode.Always });
        roots.AddRange(held.Where(static entity => entity.Parent is null));
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, new Vector2(1000f)));
        foreach (Entity root in roots)
        {
            scene.Add(root);
        }

        using SceneSimulation simulation = new(scene);
        for (int round = 0; round < 40; round++)
        {
            duringStep = random.Next(2) == 0;
            scene.Paused = random.Next(4) == 0;
            Write(!duringStep);
            if (resolveAll)
            {
                scene.InvalidateHolds();
            }

            simulation.Step(SceneFixtures.Step(tick: round));

            List<Entity> tree = [];
            foreach (Entity root in roots)
            {
                Walk(root, tree);
            }

            Assert.Equal(tree, scene.Entities.ToArray());
            int[] drawn = [.. tree.OfType<Recorder>().Select(entity => Array.IndexOf(held, entity)).OrderBy(index => Band(held[index]) + tagZ[index])];
            Assert.Equal(drawn, Order(simulation));
        }

        Assert.Equal(1, scene.Drawing.Rebuilds);
        return log;
    }

    private static void Walk(Entity entity, List<Entity> tree)
    {
        tree.Add(entity);
        foreach (Entity child in entity.Children)
        {
            Walk(child, tree);
        }
    }

    private static long Band(Entity entity) => (entity.ZIndexIsAbsolute || entity.Parent is null ? 0 : Band(entity.Parent)) + entity.ZIndex;

    private sealed class Logging(List<Entity> drawn) : Renderer
    {
        protected internal override void Draw(FrameView view) => drawn.Add(Entity!);
    }

    private sealed class Observer() : Entity(Vector2.Zero)
    {
        internal Entity[] Seen { get; private set; } = [];

        protected internal override void OnAddedToScene() => Seen = Scene!.Entities.ToArray();
    }
}
