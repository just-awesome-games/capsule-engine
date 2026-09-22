using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;
using Capsule.UI;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// The debug draw buffer is one process-wide slot, attached by whichever overlay was built last.
[Collection(LogSinkCollection.Name)]
public sealed class EngineDebugDrawTests
{
    private const double StepSeconds = 0.1;

    [Fact]
    public void AfterOneStep_TheEngineEmitsCollidersCameraAndOriginsExactlyAsTheSceneHoldsThem()
    {
        // The camera's right edge lies exactly on the boundary after the grid's second cell, so the
        // view reaches two cells, the margin adds one, and the fourth solid cell must not emit.
        Physical scene = new();
        using SceneHost host = new(SceneTransition.ToScene(typeof(Physical), null), (in SceneTransition _) => scene, new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        FrameView view = overlay.View;

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Right));
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        Assert.Equal(["Camera", "Colliders", "Origins"], overlay.Channels);
        Assert.Equal(1, scheduler.Tick);

        overlay.ToggleChannel("Colliders");
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        ReadOnlySpan<LineIntent> lines = view.Lines;

        // Box, circle, capsule, triangle, the disabled box, then the grid's seven visible faces.
        Assert.Equal(4 + 24 + 26 + 3 + 4 + 7, lines.Length);

        Aabb2D box = scene.Box.Collider.Bounds;
        Assert.Equal((box.Min, new Vector2(box.Max.X, box.Min.Y)), (lines[0].A, lines[0].B));
        Assert.Equal((box.Max, new Vector2(box.Min.X, box.Max.Y)), (lines[2].A, lines[2].B));

        Vector2 circleCenter = scene.Circle.Collider.Bounds.Center;
        for (int index = 4; index < 28; index++)
        {
            Assert.Equal(scene.Circle.Collider.Shape.Radius, Vector2.Distance(circleCenter, lines[index].A), 3);
        }

        Shape2D capsule = scene.Collision.WorldShapeOf(scene.Capsule.Collider.Handle);
        Vector2 across = new(0f, capsule.Radius);
        Assert.Equal((capsule.Point(0) + across, capsule.Point(1) + across), (lines[52].A, lines[52].B));
        Assert.Equal((capsule.Point(0) - across, capsule.Point(1) - across), (lines[53].A, lines[53].B));

        Shape2D triangle = scene.Collision.WorldShapeOf(scene.Triangle.Collider.Handle);
        for (int corner = 0; corner < 3; corner++)
        {
            Assert.Equal((triangle.Point(corner), triangle.Point((corner + 1) % 3)), (lines[54 + corner].A, lines[54 + corner].B));
        }

        Assert.Equal(new ColorRgba(0, 255, 0, 128), lines[57].Color);
        Assert.Equal(new ColorRgba(0, 255, 0), lines[0].Color);

        // Cell 0 owes three faces, cells 1 and 2 two each; the run's shared faces are not drawn.
        GridCollider2D grid = scene.Map.Collision!;
        Aabb2D first = grid.CellBounds(0, 0);
        Assert.Equal((first.Min, new Vector2(first.Min.X, first.Max.Y)), (lines[61].A, lines[61].B));
        Assert.Equal((first.Min, new Vector2(first.Max.X, first.Min.Y)), (lines[62].A, lines[62].B));
        Aabb2D third = grid.CellBounds(2, 0);
        Assert.Equal((new Vector2(third.Min.X, third.Max.Y), third.Max), (lines[67].A, lines[67].B));

        overlay.ToggleChannel("Colliders");
        overlay.ToggleChannel("Camera");
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        lines = view.Lines;

        Rect bounds = scene.Camera.Bounds!.Value;
        Assert.Equal(4, lines.Length);
        Assert.Equal((bounds.Position, new Vector2(bounds.Right, bounds.Top)), (lines[0].A, lines[0].B));

        overlay.ToggleChannel("Camera");
        overlay.ToggleChannel("Origins");
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        lines = view.Lines;

        // Every world entity gets a cross; the screen entity, whose position is canvas pixels, none.
        Assert.Equal((scene.Entities.Length - 1) * 2, lines.Length);
        Vector2 origin = scene.Box.Position;
        Assert.Equal((origin - new Vector2(1.5f, 0f), origin + new Vector2(1.5f, 0f)), (lines[0].A, lines[0].B));
        Assert.Equal((origin - new Vector2(0f, 1.5f), origin + new Vector2(0f, 1.5f)), (lines[1].A, lines[1].B));
    }

    // Held before its first step, the run has drawn nothing into the buffer; opening Debug Draw
    // asks the scene to emit as it stands, so the channels list without a tick, and a toggle shows
    // its draws on the very next frame — stamped as the settled step's pass, so the step that
    // follows replaces them rather than doubling them.
    [Fact]
    public void AHeldRun_ListsItsChannelsAndDrawsAToggleWithoutAStep()
    {
        Physical scene = new(withLowercaseChannel: true);
        using SceneHost host = new(SceneTransition.ToScene(typeof(Physical), null), (in SceneTransition _) => scene, new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        FrameView view = overlay.View;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.D);

        Assert.Equal("Debug Draw", overlay.Title);

        // Ordinal would sort every capitalized channel above "extra". The overlay reads channel
        // names the way a person does.
        Assert.Equal(["Camera", "Colliders", "extra", "Origins"], overlay.Channels);
        Assert.Equal(0, scheduler.Tick);

        overlay.ToggleChannel("Colliders");
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        // The shapes, but none of the grid's faces: the camera's visible region is empty until its
        // first late step, so the map has nothing in view to outline yet.
        Assert.Equal(0, scheduler.Tick);
        Assert.Equal(61, view.Lines.Length);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal(68, view.Lines.Length);
    }

    // The box walks two units a step. Held, the frame is settled and its collider sits where the
    // step left it; with the run going and half a step accumulated, the frame is drawn halfway and
    // so is the collider, on the sprite it follows.
    [Fact]
    public void AMovingCollider_IsDrawnWhereTheFrameDrawsItsEntity()
    {
        Physical scene = new();
        using SceneHost host = new(SceneTransition.ToScene(typeof(Physical), null), (in SceneTransition _) => scene, new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        FrameView view = overlay.View;
        overlay.ToggleChannel("Colliders");

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Right));

        Vector2 motion = scene.Box.Position - scene.Box.PreviousTransform.Position;
        Assert.Equal(new Vector2(2f, 0f), motion);
        Assert.Equal(scene.Box.Collider.Bounds.Min, view.Lines[0].A);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));
        Assert.False(overlay.IsOpen);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty, StepSeconds / 2);

        Assert.Equal(0.5f, scheduler.InterpolationAlpha);
        Assert.Equal(scene.Box.Collider.Bounds.Min - (motion * 0.5f), view.Lines[0].A);
    }

    // The hook runs once a step, after the late step, and only while a buffer is attached: with
    // none, a headless run never reaches it.
    [Fact]
    public void OnDebugDraw_RunsOncePerStepAfterTheLateStepAndOnlyWhileABufferIsAttached()
    {
        List<string> log = [];
        Scene scene = new HookScene(log);
        using SceneHost host = new(SceneTransition.ToScene(typeof(HookScene), null), (in SceneTransition _) => scene, new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Right));
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Right));

        Assert.Equal(["late", "debug", "late", "debug"], log);

        DebugDraw.UseBuffer(null);
        log.Clear();
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Right));

        Assert.Equal(["late"], log);
    }

    // The cross is the engine's, drawn before the hook: an override that never calls the base
    // still gets it.
    [Fact]
    public void AnOverrideThatSkipsTheBase_StillGetsItsOriginsCross()
    {
        Scene scene = new SilentScene();
        using SceneHost host = new(SceneTransition.ToScene(typeof(SilentScene), null), (in SceneTransition _) => scene, new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        FrameView view = overlay.View;
        overlay.ToggleChannel("Origins");
        overlay.ToggleChannel("Own");

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Right));
        ReadOnlySpan<LineIntent> lines = view.Lines;

        Assert.Equal(3, lines.Length);
        Vector2 origin = scene.Entities[0].Position;
        Assert.Equal((origin - new Vector2(1.5f, 0f), origin + new Vector2(1.5f, 0f)), (lines[0].A, lines[0].B));
        Assert.Equal((origin - new Vector2(0f, 1.5f), origin + new Vector2(0f, 1.5f)), (lines[1].A, lines[1].B));
        Assert.Equal((origin, origin + Vector2.One), (lines[2].A, lines[2].B));
    }

    private sealed class Physical : Scene
    {
        // withLowercaseChannel names a channel a game's own debug draw might: this is the channel
        // list's mixed-case ordering test, not a shape to render.
        internal Physical(bool withLowercaseChannel = false)
        {
            Camera.ViewportSize = new Vector2(32f, 32f);
            Camera.Center = new Vector2(16f, 40f);
            Camera.Bounds = new Rect(-100f, -100f, 200f, 200f);

            Box = new Holder<BoxCollider2D>(new Vector2(40f, 40f), new BoxCollider2D(new Vector2(4f, 2f))) { Velocity = new Vector2(2f, 0f) };
            Circle = new Holder<CircleCollider2D>(new Vector2(60f, 40f), new CircleCollider2D(2f));
            Capsule = new Holder<CapsuleCollider2D>(new Vector2(80f, 40f), new CapsuleCollider2D(Vector2.Zero, new Vector2(6f, 0f), 1f))
            {
                Collider = { Offset = new Vector2(1f, 1f) },
            };
            Triangle = new Holder<PolygonCollider2D>(
                new Vector2(100f, 40f),
                new PolygonCollider2D([Vector2.Zero, new Vector2(4f, 0f), new Vector2(0f, 4f)]));
            Disabled = new Holder<BoxCollider2D>(new Vector2(41f, 41f), new BoxCollider2D(new Vector2(2f, 2f)))
            {
                Collider = { Enabled = false },
            };
            Map = new TileMap(SceneFixtures.TerrainGrid("########"));

            Add(Box);
            Add(Circle);
            Add(Capsule);
            Add(Triangle);
            Add(Disabled);
            Add(Map);
            Add(new ScreenEntity(Anchor.Center, new Vector2(500f, 500f)));

            if (withLowercaseChannel)
            {
                Add(new LowercaseChannel(new Vector2(120f, 40f)));
            }
        }

        internal Holder<BoxCollider2D> Box { get; }

        internal Holder<CircleCollider2D> Circle { get; }

        internal Holder<CapsuleCollider2D> Capsule { get; }

        internal Holder<PolygonCollider2D> Triangle { get; }

        internal Holder<BoxCollider2D> Disabled { get; }

        internal TileMap Map { get; }
    }

    private sealed class HookScene : Scene
    {
        internal HookScene(List<string> log)
        {
            Entity carrier = new Holder<BoxCollider2D>(Vector2.Zero, new BoxCollider2D(Vector2.One));
            carrier.Add(new Hooked(log));
            Add(carrier);
        }
    }

    private sealed class SilentScene : Scene
    {
        internal SilentScene() => Add(new Silent(new Vector2(40f, 40f)));
    }

    private sealed class Silent(Vector2 position) : Entity(position)
    {
        protected internal override void OnDebugDraw() => DebugDraw.Line("Own", Position, Position + Vector2.One);
    }

    // A game names its own channels however it likes. This one is lowercase. It shows the overlay
    // reading it alongside the engine's capitalized channels the way a reader would.
    private sealed class LowercaseChannel(Vector2 position) : Entity(position)
    {
        protected internal override void OnDebugDraw() => DebugDraw.Line("extra", Position, Position + Vector2.One);
    }

    private sealed class Hooked(List<string> log) : Component
    {
        protected internal override void OnLateStep(in StepContext context) => log.Add("late");

        protected internal override void OnDebugDraw() => log.Add("debug");
    }

    private sealed class Holder<TCollider> : Entity
        where TCollider : Collider2D
    {
        internal Holder(Vector2 position, TCollider collider)
            : base(position)
        {
            Collider = collider;
            Add(collider);
        }

        internal TCollider Collider { get; }

        internal Vector2 Velocity { get; init; }

        protected internal override void OnStep(in StepContext context) => Position += Velocity;
    }
}
