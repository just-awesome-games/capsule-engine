using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

// A box is a rect on a frame. The renderer drawing that frame writes a plain collider over it, live
// only while the frame carries it.
public sealed class SpriteBoxTests
{
    private const string Hurt = "hurt";
    private const string Enemy = "enemy";

    private static readonly TextureHandle Sheet = new("player", ".png");

    // Every frame is 8x8 pivoted bottom-centre. The full box covers the frame and the small box its
    // lower left.
    private static readonly Sprite FrameFull = Frame(0, new SpriteMarks(boxes: [new(Hurt, new Rect(0f, 0f, 8f, 8f))]));
    private static readonly Sprite FrameSmall = Frame(1, new SpriteMarks(boxes: [new(Hurt, new Rect(1f, 2f, 3f, 6f))]));
    private static readonly Sprite FrameBare = Frame(2, null);

    // Leaving a carrying frame disables the box and ends its contact. Entering one reports the
    // contact on that same step, because contacts settle after the animator steps.
    [Fact]
    public void ABox_IsLiveOnlyOnFramesCarryingItAndReportsOnTheStepItsFrameIsEntered()
    {
        Fighter fighter = new();
        SimulationHost run = Simulate(fighter);
        BoxCollider2D box = fighter.Renderer.Box(Hurt);
        List<string> log = Log(box);

        Assert.False(box.Enabled);

        fighter.Animator.Play(new SpriteClip([FrameFull, FrameBare, FrameFull], [1, 1, 1]));
        Assert.True(box.Enabled);

        run.Step();
        Assert.Equal(["+"], log);

        run.Step();
        Assert.False(box.Enabled);
        Assert.Equal(["+", "-"], log);

        run.Step();
        Assert.Equal(["+", "-", "+"], log);
    }

    // The rect is measured from the pivot in the entity's own space, and a flip mirrors it about the
    // pivot as it mirrors the drawn frame.
    [Theory]
    [InlineData(false, -3f, -6f, -1f, -2f)]
    [InlineData(true, 1f, -6f, 3f, -2f)]
    public void ABox_IsPlacedAsTheDrawnFrameUnderFlip(bool flipX, float left, float top, float right, float bottom)
    {
        Entity body = new Root(new Vector2(100f, 50f));
        SpriteRenderer renderer = new(FrameSmall);
        body.Add(renderer);
        BoxCollider2D box = renderer.Box(Hurt);

        renderer.FlipX = flipX;

        Assert.True(box.Enabled);
        Assert.Equal(new Vector2(100f + left, 50f + top), box.Bounds.Min);
        Assert.Equal(new Vector2(100f + right, 50f + bottom), box.Bounds.Max);
    }

    [Fact]
    public void DetachingTheRenderer_TakesItsBoxesOutOfTheWorldUntilItReturnsToTheSameEntity()
    {
        Fighter fighter = new();
        SimulationHost run = Simulate(fighter);
        BoxCollider2D box = fighter.Renderer.Box(Hurt);
        List<string> log = Log(box);
        fighter.Animator.Play(new SpriteClip([FrameFull], [4]));
        run.Step();

        fighter.Remove(fighter.Renderer);
        run.Step();

        Assert.False(box.Enabled);
        Assert.Equal(["+", "-"], log);

        // The box's child sits under the fighter. Another entity cannot take the renderer.
        Assert.Throws<InvalidOperationException>(() => new Entity().Add(fighter.Renderer));

        fighter.Add(fighter.Renderer);
        run.Step();

        Assert.True(box.Enabled);
        Assert.Equal(["+", "-", "+"], log);
    }

    internal static SimulationHost Simulate(Fighter fighter, int targets = 1)
    {
        SceneFixtures.HookScene scene = new();
        scene.Add(fighter);
        scene.Add(new Target(new Vector2(-3f, -4f)));
        if (targets > 1)
        {
            scene.Add(new Target(new Vector2(-2f, -5f)));
        }

        return new SimulationHost(scene);
    }

    internal static List<string> Log(BoxCollider2D box)
    {
        box.Detects = new(Enemy);
        box.ReportsContacts = true;

        List<string> log = [];
        box.ContactEntered += _ => log.Add("+");
        box.ContactExited += _ => log.Add("-");

        return log;
    }

    internal static Sprite Frame(int index, SpriteMarks? marks) =>
        new(Sheet, new TextureRegion(index * 8, 0, 8, 8), new Vector2(4f, 8f), marks);

    // At the origin, where the full box covers (-4, -8) to (4, 0).
    internal sealed class Fighter : Entity
    {
        internal SpriteRenderer Renderer { get; }

        internal SpriteAnimator Animator { get; }

        internal Fighter()
            : base(Vector2.Zero)
        {
            Renderer = new SpriteRenderer(FrameBare);
            Animator = new SpriteAnimator(Renderer);
            Add(Renderer);
            Add(Animator);
        }
    }

    // Each target the tests place overlaps the full box and the small one alike.
    internal sealed class Target : Entity
    {
        internal Target(Vector2 position)
            : base(position)
        {
            Add(new BoxCollider2D(new Vector2(1f, 1f)) { Layer = Enemy });
        }
    }

    private sealed class Root(Vector2 position) : Entity(position);
}
