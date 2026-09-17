using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

// A socket is a point on a frame; the renderer drawing that frame places a child entity at it.
// What is held here: where the child lands, when it moves, and what the tree does to it above.
public sealed class SpriteSocketTests
{
    private const string Muzzle = "muzzle";

    private const float Tolerance = 1e-5f;

    private static readonly TextureHandle Sheet = new("player", ".png");

    // Every frame is 8x8 pivoted bottom-centre; the muzzle sits on the right edge, bobbing.
    private static readonly SpriteSocket[] Mid = [new(Muzzle, new Vector2(8f, 4f))];
    private static readonly SpriteSocket[] High = [new(Muzzle, new Vector2(8f, 3f))];
    private static readonly SpriteSocket[] Low = [new(Muzzle, new Vector2(8f, 5f))];

    private static readonly Sprite FrameMid = Frame(0, Mid);
    private static readonly Sprite FrameHigh = Frame(1, High);
    private static readonly Sprite FrameLow = Frame(2, Low);
    private static readonly Sprite FrameBare = Frame(3, []);

    [Fact]
    public void ABoundSocket_IsOneChildOfTheRenderersEntityNamedForIt()
    {
        SpriteRenderer renderer = new(FrameMid);
        Assert.Throws<InvalidOperationException>(() => renderer.Socket(Muzzle));

        Root root = new(Vector2.Zero);
        root.Add(renderer);
        Entity muzzle = renderer.Socket(Muzzle);

        Assert.Same(muzzle, renderer.Socket(Muzzle));
        Assert.Same(root, muzzle.Parent);
        Assert.Equal(Muzzle, muzzle.Name);
        Assert.Same(muzzle, root.Children[0]);
        Assert.Throws<ArgumentException>(() => renderer.Socket(string.Empty));
    }

    // The clip's frames each carry the socket at a different height; the child follows the frame
    // drawn on each step, placed by the entity as the frame is.
    [Fact]
    public void SteppingAClip_PlacesTheChildAtEachFramesSocketFromThePivot()
    {
        (Root root, SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating(new Vector2(100f, 50f));
        Entity muzzle = renderer.Socket(Muzzle);

        animator.Play(new SpriteClip([FrameMid, FrameHigh, FrameLow], [1, 1, 1], loop: true));

        // Play draws the first frame at once: the socket is placed in the same call.
        Assert.Equal(root.Position + new Vector2(4f, -4f), muzzle.WorldPosition);

        run.Step();
        Assert.Equal(FrameMid, renderer.Sprite);
        Assert.Equal(root.Position + new Vector2(4f, -4f), muzzle.WorldPosition);

        run.Step();
        Assert.Equal(FrameHigh, renderer.Sprite);
        Assert.Equal(root.Position + new Vector2(4f, -5f), muzzle.WorldPosition);

        run.Step();
        Assert.Equal(FrameLow, renderer.Sprite);
        Assert.Equal(root.Position + new Vector2(4f, -3f), muzzle.WorldPosition);
    }

    [Fact]
    public void AFlipMirrorsTheSocketAboutThePivotAndAnOffsetShiftsIt()
    {
        Root root = new(Vector2.Zero);
        SpriteRenderer renderer = new(FrameMid);
        root.Add(renderer);
        Entity muzzle = renderer.Socket(Muzzle);

        Assert.Equal(new Vector2(4f, -4f), muzzle.Position);

        renderer.FlipX = true;
        Assert.Equal(new Vector2(-4f, -4f), muzzle.Position);

        renderer.FlipY = true;
        Assert.Equal(new Vector2(-4f, 4f), muzzle.Position);

        renderer.FlipX = false;
        renderer.FlipY = false;
        renderer.Offset = new Vector2(10f, 20f);
        Assert.Equal(new Vector2(14f, 16f), muzzle.Position);
    }

    // The tree places the socket: a facing written as a negative scale mirrors it once, through the
    // parent, and a turned parent carries it round. Nothing is folded in twice.
    [Fact]
    public void AParentsScaleAndTurn_PlaceTheSocketAsTheyPlaceTheFrame()
    {
        Root facing = new(new Vector2(100f, 100f)) { Scale = new Vector2(-1f, 1f) };
        SpriteRenderer renderer = new(FrameMid);
        facing.Add(renderer);
        Entity muzzle = renderer.Socket(Muzzle);

        Assert.Equal(new Vector2(4f, -4f), muzzle.Position);
        Assert.Equal(new Vector2(96f, 96f), muzzle.WorldPosition);

        Root turned = new(new Vector2(100f, 100f)) { Rotation = MathF.PI / 2f };
        SpriteRenderer turnedRenderer = new(FrameMid);
        turned.Add(turnedRenderer);
        Vector2 placed = turnedRenderer.Socket(Muzzle).WorldPosition;

        // A quarter turn clockwise in Y-down space takes (4, -4) to (4, 4).
        Assert.Equal(104f, placed.X, Tolerance);
        Assert.Equal(104f, placed.Y, Tolerance);
    }

    // A point is a property of a discrete frame: the child snaps to the new frame's socket and
    // the frame after a change interpolates from nowhere else.
    [Fact]
    public void AFrameChange_TeleportsTheChild()
    {
        (_, SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating(Vector2.Zero);
        Entity muzzle = renderer.Socket(Muzzle);

        animator.Play(new SpriteClip([FrameMid, FrameLow], [1, 1], loop: true));
        run.Step();
        run.Step();

        Assert.Equal(FrameLow, renderer.Sprite);
        Assert.Equal(muzzle.Transform, muzzle.PreviousTransform);
        Assert.Equal(new Vector2(4f, -3f), muzzle.Position);
    }

    // Sparse authoring: a frame without the socket leaves the child on the last frame's point, and a
    // flip written on that frame mirrors that point. Until any frame carries it, the child is at
    // the origin whatever the offset.
    [Fact]
    public void AFrameWithoutTheSocket_LeavesTheChildWhereTheLastCarryingFramePutIt()
    {
        Root root = new(Vector2.Zero);
        SpriteRenderer renderer = new(FrameBare) { Offset = new Vector2(3f, 3f) };
        root.Add(renderer);
        Entity muzzle = renderer.Socket(Muzzle);

        Assert.Equal(Vector2.Zero, muzzle.Position);

        renderer.Sprite = FrameHigh;
        Assert.Equal(new Vector2(7f, -2f), muzzle.Position);

        renderer.Sprite = FrameBare;
        Assert.Equal(new Vector2(7f, -2f), muzzle.Position);

        renderer.FlipX = true;
        Assert.Equal(new Vector2(-1f, -2f), muzzle.Position);
    }

    // Two reads of a frame over one socket table are the same frame, so an animator comparing
    // frames or a test asserting one sees no difference from the table.
    [Fact]
    public void SpritesOverOneSocketTable_AreEqual()
    {
        Sprite first = new(Sheet, new TextureRegion(0, 0, 8, 8), new Vector2(4f, 8f), Mid);
        Sprite second = new(Sheet, new TextureRegion(0, 0, 8, 8), new Vector2(4f, 8f), Mid);

        Assert.Equal(first, second);
        Assert.NotEqual(first, first with { Sockets = High });
        Assert.NotEqual(first, first with { Sockets = default });
    }

    private static (Root Root, SpriteRenderer Renderer, SpriteAnimator Animator, SimulationHost Run) Animating(Vector2 at)
    {
        Root root = new(at);
        SpriteRenderer renderer = new(FrameBare);
        SpriteAnimator animator = new(renderer);
        root.Add(renderer);
        root.Add(animator);

        SceneFixtures.HookScene scene = new();
        scene.Add(root);

        return (root, renderer, animator, new SimulationHost(scene));
    }

    private static Sprite Frame(int index, SpriteSocket[] sockets) =>
        new(Sheet, new TextureRegion(index * 8, 0, 8, 8), new Vector2(4f, 8f), sockets);

    private sealed class Root(Vector2 position) : Entity(position);
}
