using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Runtime.DevTools;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.UI;
using static Capsule.Tests.DevTools.OverlayFixtures;

namespace Capsule.Tests.DevTools;

// The entity whose panel is on top is the selection. The overlay highlights it while it holds the run, and
// F flies the overlay's view to it.
public sealed class OverlaySelectionTests
{
    // One frame of a flight. A handful of them cross the longest flight.
    private const double FlightFrameMs = 100;

    // Over the world, clear of the menu.
    private static readonly Vector2 WorldPoint = new(700f, 300f);

    private static readonly ColorRgba ColliderColor = DebugDraw.ColorOf(DebugDraw.Colliders);
    private static readonly ColorRgba OriginColor = DebugDraw.ColorOf(DebugDraw.Origins);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASelection_DrawsItsSubtreeWithEveryChannelOffOutlinesItsRenderersAndEndsWithThePanel(bool hidden)
    {
        Stage stage = new();
        using OverlayRig rig = Framing(stage);
        OpenPanel(rig, "Hero");
        if (hidden)
        {
            rig.Press(Key.H);

            Assert.True(rig.Overlay.IsHidden);
        }

        rig.Frame();
        FrameView view = rig.Overlay.View;

        // The hero and its hat: a collider outline and an origin cross each, and a renderer outline each.
        // The other piece's draws stay behind the toggles.
        Assert.False(rig.Overlay.Draws.AnyEnabled);
        Assert.Equal(8, Count(view, ColliderColor));
        Assert.Equal(4, Count(view, OriginColor));
        Assert.Equal(4, Count(view, DebugDrawRenderer.SelectionColor));
        Assert.Equal(4, Count(view, DebugDrawRenderer.ChildSelectionColor));
        Assert.Equal(20, view.Lines.Length);
        Assert.All(view.Lines.ToArray(), static line => Assert.True(line.A.X < 100f && line.B.X < 100f));
        AssertOutlined(view, stage.Hero.Rect.Bounds, DebugDrawRenderer.SelectionColor);
        AssertOutlined(view, stage.Hat.Rect.Bounds, DebugDrawRenderer.ChildSelectionColor);

        if (hidden)
        {
            rig.Press(Key.H);
        }

        rig.Press(Key.Backspace);

        Assert.True(view.Lines.IsEmpty);

        OpenPanel(rig, "Hero");
        rig.Frame();

        Assert.Equal(20, view.Lines.Length);

        rig.Press(Key.Grave);

        Assert.True(view.Lines.IsEmpty);
    }

    // The held frame stands at the settled step. The outline is the sprite's rect after the move.
    [Fact]
    public void TheOutline_FollowsAnEntityThatMovedThisStep()
    {
        Stage stage = new();
        using OverlayRig rig = Framing(stage);
        stage.Hero.Velocity = new Vector2(10f, 0f);
        OpenPanel(rig, "Hero");
        float before = stage.Hero.Rect.Bounds.Left;
        rig.Press(Key.Right);
        rig.Frame();

        Assert.Equal(10f, stage.Hero.Rect.Bounds.Left - before, 3);
        AssertOutlined(rig.Overlay.View, stage.Hero.Rect.Bounds, DebugDrawRenderer.SelectionColor);
        AssertOutlined(rig.Overlay.View, stage.Hat.Rect.Bounds, DebugDrawRenderer.ChildSelectionColor);
    }

    [Theory]
    [InlineData("Hero", 320f)]
    [InlineData("Giant", 1200f)]
    public void Frame_FliesTheViewToTheSubtreesBoundsAtNoLessThanTheGameCamerasSpan(string name, float width)
    {
        Stage stage = new();
        using OverlayRig rig = Framing(stage);
        Vector2 gameCenter = stage.Camera.Center;
        Vector2 gameSize = stage.Camera.ViewportSize;
        OpenPanel(rig, name);
        Rect bounds = stage.Piece(name).Union();

        rig.Press(Key.F);
        Fly(rig, 10);

        CameraView view = rig.Host.ViewCamera!.Value;
        Assert.False(rig.Overlay.FreeCamera.Flying);
        Assert.Equal(bounds.Position.X + (bounds.Size.X / 2f), view.Center.X, 2);
        Assert.Equal(bounds.Position.Y + (bounds.Size.Y / 2f), view.Center.Y, 2);
        Assert.Equal(width, view.Size.X, 1);
        Assert.Equal(gameSize.Y / gameSize.X, view.Size.Y / view.Size.X, 4);
        Assert.Equal(gameCenter, stage.Camera.Center);
        Assert.Equal(gameSize, stage.Camera.ViewportSize);
    }

    // A held step can resize the game camera after the view has detached from it.
    [Fact]
    public void Frame_AfterTheGameCameraResized_FloorsTheSpanAtTheNewSize()
    {
        Stage stage = new();
        using OverlayRig rig = Framing(stage);
        OpenPanel(rig, "Hero");
        rig.Press(Key.F);
        Fly(rig, 10);

        Assert.Equal(320f, rig.Host.ViewCamera!.Value.Size.X, 1);

        stage.Widen = true;
        rig.Press(Key.Right);
        rig.Press(Key.F);
        Fly(rig, 10);

        Assert.Equal(640f, rig.Host.ViewCamera!.Value.Size.X, 1);
    }

    [Fact]
    public void Frame_OfAnEntityWithNoBounds_CentresItAtTheSpanTheViewHas()
    {
        Stage stage = new();
        using OverlayRig rig = Framing(stage);
        OpenPanel(rig, "Spot");

        rig.Press(Key.F);
        Fly(rig, 10);

        CameraView view = rig.Host.ViewCamera!.Value;
        Assert.Equal(stage.Spot.WorldPosition, view.Center);
        Assert.Equal(stage.Camera.ViewportSize, view.Size);
    }

    [Fact]
    public void AFarFlight_ZoomsOutToShowBothEndsAndAPanCancelsIt()
    {
        Stage stage = new();
        using OverlayRig rig = Framing(stage);
        OpenPanel(rig, "Giant");
        rig.Press(Key.F);

        float widest = 0f;
        for (int frame = 0; frame < 3; frame++)
        {
            rig.Frame(FlightFrameMs, 1);
            widest = MathF.Max(widest, rig.Host.ViewCamera!.Value.Size.X);
        }

        // Both ends are narrower: 320 wide where it starts and 1200 where it lands.
        Assert.True(rig.Overlay.FreeCamera.Flying);
        Assert.True(widest > 1200f);

        rig.PlaceWorld();
        rig.Frame(FlightFrameMs, 1, sampled: DeviceSnapshot.Empty.WithPointer(WorldPoint).With(MouseButton.Middle));
        rig.Frame(FlightFrameMs, 1, sampled: DeviceSnapshot.Empty.WithPointer(WorldPoint + new Vector2(30f, 20f)).With(MouseButton.Middle));
        CameraView panned = rig.Host.ViewCamera!.Value;

        Assert.False(rig.Overlay.FreeCamera.Flying);

        Fly(rig, 10);

        Assert.Equal(panned, rig.Host.ViewCamera!.Value);

        rig.Press(Key.F);

        Assert.True(rig.Overlay.FreeCamera.Flying);
    }

    [Theory]
    [InlineData(null, "Frame needs an entity")]
    [InlineData("Hud", "Hud is on the screen layer and has no world position")]
    public void Frame_WithNoWorldEntityOnTop_SaysWhyAndLeavesTheView(string? panel, string status)
    {
        using OverlayRig rig = Framing(new Stage());
        if (panel is null)
        {
            rig.Open();
            rig.Press(Key.S);
        }
        else
        {
            OpenPanel(rig, panel);
        }

        rig.Press(Key.F);

        Assert.Contains(status, rig.Overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Null(rig.Host.ViewCamera);
        Assert.False(rig.Overlay.FreeCamera.Flying);
    }

    private static void Fly(OverlayRig rig, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            rig.Frame(FlightFrameMs, 1);
        }
    }

    private static int Count(FrameView view, ColorRgba color)
    {
        int count = 0;
        foreach (LineIntent line in view.Lines)
        {
            if (line.Color == color)
            {
                count++;
            }
        }

        return count;
    }

    // The top and bottom edges, which together pin both corners.
    private static void AssertOutlined(FrameView view, Rect bounds, ColorRgba color)
    {
        Assert.False(bounds.IsEmpty);
        Assert.Contains(
            view.Lines.ToArray(),
            line => line.Color == color && line.A == new Vector2(bounds.Left, bounds.Top) && line.B == new Vector2(bounds.Right, bounds.Top));
        Assert.Contains(
            view.Lines.ToArray(),
            line => line.Color == color && line.A == new Vector2(bounds.Right, bounds.Bottom) && line.B == new Vector2(bounds.Left, bounds.Bottom));
    }

    // Opens the panel of the entity named name from the scene page.
    private static void OpenPanel(OverlayRig rig, string name)
    {
        rig.OpenEntityPanel(name);
        rig.PlaceWorld();
    }

    // A hero in a hat, a piece beside them to tell their draws from, a giant far away, a spot with
    // nothing to bound and a screen-layer entity. A step doubles the camera's size once Widen is set.
    private sealed class Stage : Scene
    {
        internal Piece Hero { get; } = new("Hero", Vector2.Zero, new Vector2(16f, 16f), collides: true);

        internal Piece Hat { get; } = new("Hat", new Vector2(0f, -20f), new Vector2(8f, 8f), collides: true);

        internal Piece Giant { get; } = new("Giant", new Vector2(3000f, 2000f), new Vector2(1000f, 500f), collides: false);

        internal Entity Spot { get; } = new EntityHierarchyFixtures.Node(new Vector2(-500f, 400f)) { Name = "Spot" };

        internal bool Widen { get; set; }

        internal Stage()
        {
            Camera.ViewportSize = new Vector2(320f, 180f);
            Hat.Parent = Hero;
            Add(Hero);
            Add(new Piece("Other", new Vector2(200f, 0f), new Vector2(16f, 16f), collides: true));
            Add(Giant);
            Add(Spot);
            Add(new ScreenEntity(Anchor.Center, Vector2.Zero) { Name = "Hud" });
        }

        internal Piece Piece(string name) => name == "Hero" ? Hero : Giant;

        protected override void OnStep(in StepContext context)
        {
            if (Widen)
            {
                Widen = false;
                Camera.ViewportSize = new Vector2(640f, 360f);
            }
        }
    }

    private sealed class Piece : Entity
    {
        internal ColorRect Rect { get; }

        internal BoxCollider2D? Box { get; }

        internal Vector2 Velocity { get; set; }

        internal Piece(string name, Vector2 position, Vector2 size, bool collides)
            : base(position)
        {
            Name = name;
            Rect = new ColorRect(size);
            Add(Rect);
            if (collides)
            {
                Box = new BoxCollider2D(size);
                Add(Box);
            }
        }

        // The renderer and collider bounds of this piece and the pieces under it.
        internal Rect Union()
        {
            Rect union = Rect.Bounds;
            if (Box is { } box)
            {
                union = Merge(union, Capsule.Rendering.Rect.FromEdges(box.Bounds.Min.X, box.Bounds.Min.Y, box.Bounds.Max.X, box.Bounds.Max.Y));
            }

            foreach (Entity child in Children)
            {
                union = Merge(union, ((Piece)child).Union());
            }

            return union;
        }

        protected internal override void OnStep(in StepContext context) => Position += Velocity;

        private static Rect Merge(Rect a, Rect b) =>
            Capsule.Rendering.Rect.FromEdges(MathF.Min(a.Left, b.Left), MathF.Min(a.Top, b.Top), MathF.Max(a.Right, b.Right), MathF.Max(a.Bottom, b.Bottom));
    }
}
