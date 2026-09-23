using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class EntityTransformTests
{
    // The child's world is the parent's world placing the child's locals, Transform2D's way —
    // its arithmetic, mirror rule included, is Transform2DTests' to hold; this is the wiring.
    [Fact]
    public void AChildsWorldTransform_IsTheParentsPlacingItsLocals()
    {
        Node parent = new(new Vector2(100f, 50f)) { Rotation = 0.5f, Scale = new Vector2(2f, 3f) };
        Entity child = new(parent, new Vector2(10f, -4f)) { Rotation = 0.25f, Scale = new Vector2(0.5f, 0.5f) };

        Assert.Equal(parent.WorldTransform.Compose(child.Transform), child.WorldTransform);
        Assert.Equal(child.WorldTransform.Position, child.WorldPosition);
        Assert.Equal(0.75f, child.WorldTransform.Rotation);
        Assert.Equal(new Vector2(1f, 1.5f), child.WorldTransform.Scale);
        Assert.Equal(new Vector2(10f, -4f), child.Position);

        Node mirror = new(new Vector2(30f, 40f)) { Rotation = 0.7f, Scale = new Vector2(-1f, 1f) };
        Entity mirrored = new(mirror, new Vector2(10f, 0f)) { Rotation = 0.25f };
        Assert.Equal(mirror.WorldTransform.Compose(mirrored.Transform), mirrored.WorldTransform);

        // A world position written is the local that lands there; none does under a zero axis.
        child.WorldPosition = new Vector2(130f, 20f);
        Assert.Equal(130f, child.WorldPosition.X, Tolerance);
        Assert.Equal(20f, child.WorldPosition.Y, Tolerance);
        Assert.NotEqual(new Vector2(130f, 20f), child.Position);

        Entity flat = new(new Node(Vector2.Zero) { Scale = new Vector2(0f, 1f) }, new Vector2(1f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => flat.WorldPosition = new Vector2(5f, 5f));
        Assert.Equal(new Vector2(1f, 1f), flat.Position);
    }

    // One value for the three locals: set as one write, read back composed, and rolled back
    // together where a collider beneath refuses the position.
    [Fact]
    public void SettingTransform_WritesTheThreeLocalsAsOneAndRollsBackTogether()
    {
        Node parent = new(new Vector2(10f, 0f)) { Scale = new Vector2(2f, 2f) };
        Entity child = new(parent);

        child.Transform = new Transform2D(new Vector2(1f, 1f), 0.5f, new Vector2(3f, 1f));

        Assert.Equal(new Transform2D(new Vector2(1f, 1f), 0.5f, new Vector2(3f, 1f)), child.Transform);
        Assert.Equal(new Transform2D(new Vector2(12f, 2f), 0.5f, new Vector2(6f, 2f)), child.WorldTransform);

        Node held = new(Vector2.Zero);
        held.Add(new BoxCollider2D(new Vector2(4f, 4f)));
        new SceneFixtures.HookScene().Add(held);
        Assert.Throws<ArgumentOutOfRangeException>(() => held.Transform = new Transform2D(Vector2.Zero, float.NaN));
        Assert.Equal(Transform2D.Identity, held.Transform);
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
        Node mirror = new(Vector2.Zero) { Scale = new Vector2(-1f, 1f) };
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

    [Fact]
    public void AScrollFactor_IsTheRootsAlone()
    {
        Node root = new(Vector2.Zero) { ScrollFactor = new Vector2(0.5f, 0.5f) };
        Entity child = new(root);

        Assert.Equal(new Vector2(0.5f, 0.5f), child.ScrollFactor);
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => child.ScrollFactor = new Vector2(0.25f, 1f));
        Assert.Contains("Set the scroll factor on the root", refused.Message, StringComparison.Ordinal);

        // A child that collides cannot sit under a scrolled root, from either side.
        Node collides = new(Vector2.Zero);
        collides.Add(new BoxCollider2D(new Vector2(4f, 4f)));
        Assert.Throws<InvalidOperationException>(() => collides.Parent = root);
        Assert.Throws<InvalidOperationException>(() => child.Add(new BoxCollider2D(new Vector2(4f, 4f))));

        Node plain = new(Vector2.Zero);
        collides.Parent = plain;
        Assert.Throws<InvalidOperationException>(() => plain.ScrollFactor = new Vector2(0.5f, 0.5f));
    }

    // Relative bands: a child's ZIndex adds to its parent's, so a child at -1 under a parent at 5
    // still draws over a root at 3, and a band written above re-sorts the subtree beneath it.
    [Fact]
    public void AChildsZIndex_IsRelativeToItsParents()
    {
        Node parent = new(Vector2.Zero) { ZIndex = 5 };
        parent.Add(Tag(2));
        Entity child = new(parent) { ZIndex = -1 };
        child.Add(Tag(1));
        Node root = new(Vector2.Zero) { ZIndex = 3 };
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

    private static Vector2 Turned(Vector2 origin, Vector2 local, float radians) =>
        new(
            origin.X + (local.X * DeterministicMath.Cos(radians)) - (local.Y * DeterministicMath.Sin(radians)),
            origin.Y + (local.X * DeterministicMath.Sin(radians)) + (local.Y * DeterministicMath.Cos(radians)));

    private static SpriteRenderer Tag(int tag) =>
        new(SceneFixtures.Frame(1, 1)) { Offset = new Vector2(tag, 0f) };

    private sealed class Spinner(Vector2 position, float turnPerStep) : Entity(position)
    {
        protected internal override void OnStep(in StepContext context) => Rotation += turnPerStep;
    }
}
