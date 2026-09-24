using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Physics;
using Capsule.UI;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class EntityAttachmentTests
{
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
        Node turned = new(Vector2.Zero) { Rotation = 0.5f };
        Entity child = new(turned);
        InvalidOperationException attach = Assert.Throws<InvalidOperationException>(() => child.Add(new BoxCollider2D(new Vector2(4f, 4f))));
        Assert.Contains("Node carries a rotation of 0.5", attach.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => child.Add(new VisibleOnScreenNotifier2D(new Vector2(4f, 4f))));
        Assert.Throws<InvalidOperationException>(() => child.Add(new Label(BitmapFont.Default, "hi")));

        Node collides = new(Vector2.Zero);
        collides.Add(new BoxCollider2D(new Vector2(4f, 4f)));
        Assert.Throws<InvalidOperationException>(() => collides.Parent = turned);
        Assert.Throws<InvalidOperationException>(() => collides.Parent = new Node(Vector2.Zero) { Scale = new Vector2(2f, 2f) });
        Assert.Null(collides.Parent);

        Node upright = new(Vector2.Zero);
        collides.Parent = upright;
        Entity between = new(upright);
        Node grandparent = new(Vector2.Zero);
        upright.Parent = grandparent;
        InvalidOperationException write = Assert.Throws<InvalidOperationException>(() => grandparent.Rotation = 1f);
        Assert.Contains("Node carries a rotation of 1", write.Message, StringComparison.Ordinal);
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
        Node root = new(new Vector2(10f, 10f));
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

        Assert.Throws<ArgumentOutOfRangeException>(() => root.Position = new Vector2(float.NaN, 0f));
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
    public void APanel_WritesLocalAndWorldRowsForAParentedEntity()
    {
        Scene scene = new();
        Node parent = new(new Vector2(10f, 0f)) { Scale = new Vector2(2f, 2f) };
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
                ("Visible", null),
                ("Tint", "#ffffffff"),
                ("Flash", "0"),
                ("StepMode", "Inherit"),
                ("Remove", null),
            ],
            panel.Rows.ToArray().Select(row => (row.Label, row.Value)).ToArray());
    }
}
