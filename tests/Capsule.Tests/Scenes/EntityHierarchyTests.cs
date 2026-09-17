using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Physics;
using Capsule.Tiles;
using Capsule.UI;

namespace Capsule.Tests.Scenes;

public sealed class EntityHierarchyTests
{
    private const float Tolerance = 1e-5f;

    // The child's world is the parent's world placing the child's locals, Transform2D's way —
    // its arithmetic, mirror rule included, is Transform2DTests' to hold; this is the wiring.
    [Fact]
    public void AChildsWorldTransform_IsTheParentsPlacingItsLocals()
    {
        Pivot parent = new(new Vector2(100f, 50f)) { Rotation = 0.5f, Scale = new Vector2(2f, 3f) };
        Entity child = new(parent, new Vector2(10f, -4f)) { Rotation = 0.25f, Scale = new Vector2(0.5f, 0.5f) };

        Assert.Equal(parent.WorldTransform.Then(child.Transform), child.WorldTransform);
        Assert.Equal(child.WorldTransform.Position, child.WorldPosition);
        Assert.Equal(0.75f, child.WorldTransform.Rotation);
        Assert.Equal(new Vector2(1f, 1.5f), child.WorldTransform.Scale);
        Assert.Equal(new Vector2(10f, -4f), child.Position);

        Pivot mirror = new(new Vector2(30f, 40f)) { Rotation = 0.7f, Scale = new Vector2(-1f, 1f) };
        Entity mirrored = new(mirror, new Vector2(10f, 0f)) { Rotation = 0.25f };
        Assert.Equal(mirror.WorldTransform.Then(mirrored.Transform), mirrored.WorldTransform);

        // A world position written is the local that lands there; none does under a zero axis.
        child.WorldPosition = new Vector2(130f, 20f);
        Assert.Equal(130f, child.WorldPosition.X, Tolerance);
        Assert.Equal(20f, child.WorldPosition.Y, Tolerance);
        Assert.NotEqual(new Vector2(130f, 20f), child.Position);

        Entity flat = new(new Pivot(Vector2.Zero) { Scale = new Vector2(0f, 1f) }, new Vector2(1f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => flat.WorldPosition = new Vector2(5f, 5f));
        Assert.Equal(new Vector2(1f, 1f), flat.Position);
    }

    // One value for the three locals: set as one write, read back composed, and rolled back
    // together where a collider beneath refuses the position.
    [Fact]
    public void SettingTransform_WritesTheThreeLocalsAsOneAndRollsBackTogether()
    {
        Pivot parent = new(new Vector2(10f, 0f)) { Scale = new Vector2(2f, 2f) };
        Entity child = new(parent);

        child.Transform = new Transform2D(new Vector2(1f, 1f), 0.5f, new Vector2(3f, 1f));

        Assert.Equal(new Transform2D(new Vector2(1f, 1f), 0.5f, new Vector2(3f, 1f)), child.Transform);
        Assert.Equal(new Transform2D(new Vector2(12f, 2f), 0.5f, new Vector2(6f, 2f)), child.WorldTransform);

        Pivot held = new(Vector2.Zero);
        held.Add(new BoxCollider2D(new Vector2(4f, 4f)));
        new SceneFixtures.HookScene().Add(held);
        Assert.Throws<ArgumentException>(() => held.Transform = new Transform2D(new Vector2(3e38f, 0f)));
        Assert.Equal(Transform2D.Identity, held.Transform);
        Assert.Throws<ArgumentOutOfRangeException>(() => held.Transform = new Transform2D(Vector2.Zero, float.NaN));
        Assert.Throws<InvalidOperationException>(() => held.Transform = new Transform2D(Vector2.Zero, 0.5f));
    }

    // A renderer on a child is placed by the composed transform at both ends: the previous world
    // from every previous local, the current from every current one; a turn under a mirror runs
    // the other way with the flip folded in; and a turn written between steps is both ends.
    [Fact]
    public void RenderersBeneath_DrawByTheComposedTransformAtBothEnds()
    {
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, new Vector2(1000f, 1000f)));
        Spinner pivot = new(new Vector2(100f, 100f), turnPerStep: 0.5f);
        Entity satellite = new(pivot, new Vector2(0f, -20f)) { Rotation = 0.1f };
        SpriteRenderer sprite = new(SceneFixtures.Frame(4, 4));
        satellite.Add(sprite);
        Pivot mirror = new(Vector2.Zero) { Scale = new Vector2(-1f, 1f) };
        Entity arm = new(mirror) { Rotation = 0.25f };
        arm.Add(new SpriteRenderer(SceneFixtures.Frame(4, 4)));
        scene.Add(pivot);
        scene.Add(mirror);

        using SimulationHost host = new(scene);
        host.Step();
        host.Step();

        SpriteIntent[] drawn = host.Simulation.View.Sprites.ToArray();
        Assert.Equal(Turned(new Vector2(100f, 100f), new Vector2(0f, -20f), 0.5f), drawn[0].PreviousPosition);
        Assert.Equal(Turned(new Vector2(100f, 100f), new Vector2(0f, -20f), 1f), drawn[0].Position);
        Assert.Equal(0.6f, drawn[0].PreviousRotation, Tolerance);
        Assert.Equal(1.1f, drawn[0].Rotation, Tolerance);
        Assert.Equal(drawn[0].Position, sprite.Bounds.Position + (sprite.Bounds.Size / 2f));
        Assert.Equal(-0.25f, drawn[1].Rotation);
        Assert.Equal(-0.25f, drawn[1].PreviousRotation);
        Assert.True(drawn[1].FlipX);

        pivot.Rotation = 2f;
        host.Simulation.RewriteView();

        SpriteIntent settled = host.Simulation.View.Sprites[0];
        Assert.Equal(settled.Position, settled.PreviousPosition);
        Assert.Equal(2.1f, settled.PreviousRotation, Tolerance);
    }

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
        Assert.Null(grandchild.Scene);
        Assert.Same(root, child.Parent);
        Assert.Same(child, grandchild.Parent);
    }

    [Fact]
    public void AParent_IsRefusedInASceneOrQueued_InACycle_AndOnAScreenEntityOrTileMap()
    {
        SceneFixtures.HookScene scene = new();
        Pivot root = new(Vector2.Zero);
        Pivot other = new(Vector2.Zero);
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

        Pivot queued = new(Vector2.Zero);
        using SceneSimulation simulation = new(scene);
        scene.Add(new SceneFixtures.Watcher(_ =>
        {
            if (queued.Scene is null && queued.Parent is null)
            {
                queued.Parent = root;
                Assert.Throws<InvalidOperationException>(() => queued.Parent = other);
            }
        }));
        simulation.Step(SceneFixtures.Step());

        Assert.Same(scene, queued.Scene);
        Assert.Same(root, queued.Parent);
    }

    // A group under a screen root draws on the screen layer, in canvas pixels from the root's
    // anchored point.
    [Fact]
    public void APlainEntityUnderAScreenRoot_DrawsOnTheScreenLayerFromTheAnchor()
    {
        ScreenEntity hud = new(Anchor.BottomRight, new Vector2(-40f, -20f));
        Entity item = new(hud, new Vector2(4f, 4f));
        item.Add(new ColorRect(new Vector2(2f, 2f)));

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero));
        scene.Add(hud);
        using SimulationHost host = new(scene);
        host.Step();

        Vector2 canvas = host.Simulation.View.Canvas;
        SpriteIntent drawn = Assert.Single(host.Simulation.View.ScreenSprites.ToArray());
        Assert.Empty(host.Simulation.View.Sprites.ToArray());
        Assert.Equal(canvas + new Vector2(-36f, -16f), drawn.Position);
    }

    // Each of the three sites — the attach, the parent write and the transform write — and the
    // message names the entity carrying the value, not the one holding the component. A collider,
    // body and notifier follow position alone; a label, panel or hit box scales but cannot turn.
    [Fact]
    public void AComponentThatCannotTurnOrScale_IsRefusedAtEverySite()
    {
        Pivot turned = new(Vector2.Zero) { Rotation = 0.5f };
        Entity child = new(turned);
        InvalidOperationException attach = Assert.Throws<InvalidOperationException>(() => child.Add(new BoxCollider2D(new Vector2(4f, 4f))));
        Assert.Contains("Pivot carries a rotation of 0.5", attach.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => child.Add(new VisibleOnScreenNotifier2D(new Vector2(4f, 4f))));
        Assert.Throws<InvalidOperationException>(() => child.Add(new Label(BitmapFont.Default, "hi")));

        Pivot collides = new(Vector2.Zero);
        collides.Add(new BoxCollider2D(new Vector2(4f, 4f)));
        Assert.Throws<InvalidOperationException>(() => collides.Parent = turned);
        Assert.Throws<InvalidOperationException>(() => collides.Parent = new Pivot(Vector2.Zero) { Scale = new Vector2(2f, 2f) });
        Assert.Null(collides.Parent);

        Pivot upright = new(Vector2.Zero);
        collides.Parent = upright;
        Entity between = new(upright);
        Pivot grandparent = new(Vector2.Zero);
        upright.Parent = grandparent;
        InvalidOperationException write = Assert.Throws<InvalidOperationException>(() => grandparent.Rotation = 1f);
        Assert.Contains("Pivot carries a rotation of 1", write.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => grandparent.Scale = new Vector2(1f, -1f));
        Assert.Equal(Transform2D.Identity, grandparent.Transform);

        // A sibling branch is free to turn; a panel beneath refuses a turn but takes a scale.
        between.Rotation = 0.25f;
        between.Rotation = 0f;
        Entity panelled = new(between);
        panelled.Add(new NineSlice(SceneFixtures.Frame(8, 8), new SliceInsets(2, 2, 2, 2), new Vector2(16f, 16f)));
        Assert.Throws<InvalidOperationException>(() => between.Rotation = 0.5f);
        between.Scale = new Vector2(2f, 2f);
        Assert.Equal(new Vector2(32f, 32f), panelled.Get<NineSlice>().Bounds.Size);
    }

    // A collider beneath follows the world position: an ancestor's move re-places it, a
    // placement it refuses rolls the whole write back, and a detach or removal unregisters it.
    [Fact]
    public void CollidersBeneath_FollowTheWorldPositionAndLeaveNoRegistrationBehind()
    {
        SceneFixtures.HookScene scene = new();
        Pivot root = new(new Vector2(10f, 10f));
        Entity middle = new(root, new Vector2(5f, 0f));
        Entity leaf = new(middle, new Vector2(0f, 5f));
        BoxCollider2D collider = new(new Vector2(4f, 4f));
        leaf.Add(collider);
        scene.Add(root);
        Assert.Equal(new Vector2(15f, 15f), scene.Collision.PositionOf(collider.Handle));

        root.Position = new Vector2(100f, 100f);

        Assert.Equal(new Vector2(105f, 105f), scene.Collision.PositionOf(collider.Handle));
        Assert.Equal(1, scene.Collision.OverlapBoxAll(CollisionFixtures.Box(104f, 104f, 6f, 6f), CollisionFilter.Everything, new Contact2D[4]));
        Assert.Equal(0, scene.Collision.OverlapBoxAll(CollisionFixtures.Box(14f, 14f, 6f, 6f), CollisionFilter.Everything, new Contact2D[4]));

        Assert.Throws<ArgumentException>(() => root.Position = new Vector2(3e38f, 0f));
        Assert.Equal(new Vector2(100f, 100f), root.Position);
        Assert.Equal(new Vector2(105f, 105f), scene.Collision.PositionOf(collider.Handle));

        BoxCollider2D removed = new(new Vector2(4f, 4f));
        new Entity(root, new Vector2(20f, 0f)).Add(removed);
        Assert.Equal(2, scene.Collision.ColliderCount);
        leaf.Remove(collider);
        scene.Remove(removed.Entity!);
        Assert.Equal(0, scene.Collision.ColliderCount);
        Assert.Null(collider.World);
        Assert.Null(removed.World);

        // Nothing below the root tracks movement any more, so a move re-notifies nothing stale.
        root.Position = new Vector2(50f, 50f);
        Assert.Equal(0, scene.Collision.OverlapBoxAll(CollisionFixtures.Box(0f, 0f, 200f, 200f), CollisionFilter.Everything, new Contact2D[4]));
    }

    [Fact]
    public void AScrollFactor_IsTheRootsAlone()
    {
        Pivot root = new(Vector2.Zero) { ScrollFactor = new Vector2(0.5f, 0.5f) };
        Entity child = new(root);

        Assert.Equal(new Vector2(0.5f, 0.5f), child.ScrollFactor);
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => child.ScrollFactor = new Vector2(0.25f, 1f));
        Assert.Contains("set the scroll factor on the root", refused.Message, StringComparison.Ordinal);

        // A child that collides cannot sit under a scrolled root, from either side.
        Pivot collides = new(Vector2.Zero);
        collides.Add(new BoxCollider2D(new Vector2(4f, 4f)));
        Assert.Throws<InvalidOperationException>(() => collides.Parent = root);
        Assert.Throws<InvalidOperationException>(() => child.Add(new BoxCollider2D(new Vector2(4f, 4f))));

        Pivot plain = new(Vector2.Zero);
        collides.Parent = plain;
        Assert.Throws<InvalidOperationException>(() => plain.ScrollFactor = new Vector2(0.5f, 0.5f));
    }

    // Relative bands: a child's ZIndex adds to its parent's, so a child at -1 under a parent at 5
    // still draws over a root at 3, and a band written above re-sorts the subtree beneath it.
    [Fact]
    public void AChildsZIndex_IsRelativeToItsParents()
    {
        Pivot parent = new(Vector2.Zero) { ZIndex = 5 };
        parent.Add(Tag(2));
        Entity child = new(parent) { ZIndex = -1 };
        child.Add(Tag(1));
        Pivot root = new(Vector2.Zero) { ZIndex = 3 };
        root.Add(Tag(0));

        SceneFixtures.HookScene scene = new();
        scene.Add(parent);
        scene.Add(root);

        using SimulationHost host = new(scene);
        host.Step();
        Assert.Equal([0, 1, 2], Order(host.Simulation));

        parent.ZIndex = -10;
        host.Step();
        Assert.Equal([1, 2, 0], Order(host.Simulation));
    }

    [Fact]
    public void APanel_WritesLocalAndWorldRowsForAParentedEntity()
    {
        Scene scene = new();
        Pivot parent = new(new Vector2(10f, 0f)) { Scale = new Vector2(2f, 2f) };
        Entity child = new(parent, new Vector2(1f, 1f)) { Rotation = 0.5f };
        scene.Add(parent);
        using SimulationHost host = new(scene);
        DebugPanel panel = new();

        child.RunDebugPanel(panel);

        Assert.Equal(
            [
                ("Entity", null),
                ("Transform", "(1, 1) r 0.5 s (1, 1)"),
                ("World Transform", "(12, 2) r 0.5 s (2, 2)"),
                ("ZIndex", "0"),
                ("ScrollFactor", "(1, 1)"),
                ("Remove", null),
            ],
            panel.Rows.ToArray().Select(row => (row.Label, row.Value)).ToArray());
    }

    private static Vector2 Turned(Vector2 origin, Vector2 local, float radians) =>
        new(
            origin.X + (local.X * DeterministicMath.Cos(radians)) - (local.Y * DeterministicMath.Sin(radians)),
            origin.Y + (local.X * DeterministicMath.Sin(radians)) + (local.Y * DeterministicMath.Cos(radians)));

    private static SpriteRenderer Tag(int tag) =>
        new(SceneFixtures.Frame(1, 1)) { Offset = new Vector2(tag, 0f) };

    private static int[] Order(SceneSimulation simulation)
    {
        ReadOnlySpan<SpriteIntent> sprites = simulation.View.Sprites;
        int[] tags = new int[sprites.Length];
        for (int index = 0; index < tags.Length; index++)
        {
            tags[index] = (int)sprites[index].Position.X;
        }

        return tags;
    }

    private sealed class Pivot(Vector2 position) : Entity(position);

    private sealed class Spinner(Vector2 position, float turnPerStep) : Entity(position)
    {
        protected internal override void OnStep(in StepContext context) => Rotation += turnPerStep;
    }

    private sealed class Mover : Component
    {
        protected internal override void OnStep(in StepContext context) => Entity!.Position += Vector2.UnitX;
    }

    // Constructed before its parent is added, and reads the parent's world position as it steps.
    private sealed class Follower(Entity parent, List<string> log) : Entity(parent)
    {
        protected internal override void OnStep(in StepContext context) => log.Add(DebugPanel.Format(Parent!.WorldPosition));
    }
}
