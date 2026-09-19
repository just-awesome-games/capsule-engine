using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tiles;
using Capsule.UI;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class EntityTreeTests
{
    // Construction order is the parent after its first child, but the scene holds, starts and
    // steps in tree order, so a child reads the world position its parent moved to this step.
    [Fact]
    public void ASubtree_StepsAndStartsInTreeOrder()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        SceneFixtures.Recorder root = new("root", log, logsStart: true);
        Entity follower = new Follower(root, log);
        SceneFixtures.Recorder first = new("first", log, logsStart: true) { Parent = root };
        SceneFixtures.Recorder grandchild = new("grandchild", log, logsStart: true) { Parent = first };
        root.Add(new Mover());

        scene.Add(root);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([root, follower, first, grandchild], scene.Entities.ToArray());
        Assert.Equal(
            [
                "root+", "first+", "grandchild+",
                "root!", "first!", "grandchild!",
                "root", "(1, 0)", "first", "grandchild",
                "root.late", "first.late", "grandchild.late",
            ],
            log);
    }

    // Parenting under an entity the scene holds mid-step joins through the deferred pass, exactly
    // as Scene.Add would, and lands directly after the parent's subtree rather than at the end.
    [Fact]
    public void ParentingUnderAnInSceneEntityMidStep_JoinsAtTheDrainAfterTheParentsSubtree()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        SceneFixtures.Recorder root = new("root", log);
        SceneFixtures.Recorder other = new("other", log);
        Entity? child = null;
        scene.Add(root);
        scene.Add(new SceneFixtures.Watcher(_ => child ??= new SceneFixtures.Recorder("child", log, logsStart: true) { Parent = root }));
        scene.Add(other);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Same(scene, child!.Scene);
        Assert.Contains("child!", log);
        Assert.Equal([root, child], scene.Entities[..2].ToArray());
        Assert.Equal(other, scene.Entities[^1]);

        log.Clear();
        simulation.Step(SceneFixtures.Step());
        Assert.Equal(["root", "child", "other", "root.late", "child.late", "other.late"], log);
    }

    // A parent's removal takes its subtree, children first, and keeps the links; a child removed
    // on its own lets go of its parent, so it can be parented again or added as a root.
    [Fact]
    public void Removal_TakesTheSubtreeChildrenFirst_AndAChildAloneLetsGoOfItsParent()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        SceneFixtures.Recorder root = new("root", log);
        SceneFixtures.Recorder child = new("child", log) { Parent = root };
        SceneFixtures.Recorder grandchild = new("grandchild", log) { Parent = child };
        Entity loner = new(root);
        scene.Add(root);
        log.Clear();

        scene.Remove(loner);
        Assert.Null(loner.Parent);
        Assert.Equal([child], root.Children.ToArray());
        scene.Add(loner);
        Assert.Equal([root, child, grandchild, loner], scene.Entities.ToArray());

        scene.Remove(root);
        Assert.Equal(["grandchild-", "child-", "root-"], log);
        Assert.Equal([loner], scene.Entities.ToArray());
        Assert.Null(grandchild.SceneOrNull);
        Assert.Same(root, child.Parent);
        Assert.Same(child, grandchild.Parent);
    }

    [Fact]
    public void AParent_IsRefusedInASceneOrQueued_InACycle_AndOnAScreenEntityOrTileMap()
    {
        SceneFixtures.HookScene scene = new();
        Node root = new(Vector2.Zero);
        Node other = new(Vector2.Zero);
        Entity child = new(root);
        Entity grandchild = new(child);

        Assert.Throws<InvalidOperationException>(() => root.Parent = grandchild);
        Assert.Throws<InvalidOperationException>(() => root.Parent = root);
        Assert.Throws<InvalidOperationException>(() => new ScreenEntity(Anchor.TopLeft, Vector2.Zero).Parent = root);
        Assert.Throws<InvalidOperationException>(() => new TileMap(SceneFixtures.RoomGrid()).Parent = root);
        Assert.Throws<InvalidOperationException>(() => scene.Add(child));

        scene.Add(root);
        scene.Add(other);
        Assert.Throws<InvalidOperationException>(() => child.Parent = other);
        Assert.Throws<InvalidOperationException>(() => child.Parent = null);

        Node queued = new(Vector2.Zero);
        using SceneSimulation simulation = new(scene);
        scene.Add(new SceneFixtures.Watcher(_ =>
        {
            if (queued.SceneOrNull is null && queued.Parent is null)
            {
                queued.Parent = root;
                Assert.Throws<InvalidOperationException>(() => queued.Parent = other);
            }
        }));
        simulation.Step(SceneFixtures.Step());

        Assert.Same(scene, queued.Scene);
        Assert.Same(root, queued.Parent);
    }

    // Constructed before its parent is added, and reads the parent's world position as it steps.
    private sealed class Follower(Entity parent, List<string> log) : Entity(parent)
    {
        protected internal override void OnStep(in StepContext context) => log.Add(DebugPanel.Format(Parent!.WorldPosition));
    }
}
