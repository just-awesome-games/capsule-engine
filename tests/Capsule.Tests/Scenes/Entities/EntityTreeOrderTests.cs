using System.Numerics;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;
using Recorder = Capsule.Tests.Scenes.SceneFixtures.Recorder;
using Watcher = Capsule.Tests.Scenes.SceneFixtures.Watcher;

namespace Capsule.Tests.Scenes;

public sealed class EntityTreeOrderTests
{
    [Fact]
    public void ARootThatLeavesAndReturnsRepeatedly_HoldsOneEntryAtTheEndOfTheRoots()
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

    // Children joining held parents in one drain land mid-list. The drain places them as eager inserts
    // would, and starts them parent-first in tree order.
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

    private sealed class Observer() : Entity(Vector2.Zero)
    {
        internal Entity[] Seen { get; private set; } = [];

        protected internal override void OnAddedToScene() => Seen = Scene!.Entities.ToArray();
    }
}
