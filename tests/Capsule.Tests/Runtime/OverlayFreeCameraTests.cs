using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using static Capsule.Tests.Runtime.OverlayFixtures;

namespace Capsule.Tests.Runtime;

// The free camera stands in for the game's view and never touches the run.
public sealed class OverlayFreeCameraTests
{
    // Over the world, clear of the menu, on the output's letterboxed world region.
    private static readonly Vector2 WorldPoint = new(700f, 300f);

    // Held before the first step, the game's frame carries an unconfined centre the bounds confine,
    // then the offset moves it. Expand grows the span on the wide output.
    [Theory]
    [InlineData(ViewportFit.Letterbox)]
    [InlineData(ViewportFit.Expand)]
    public void Detaching_PlacesTheSameWorldRectAsTheGamesFrame(ViewportFit fit)
    {
        using OverlayRig rig = Framing(new FramedScene(fit));
        rig.Open();
        Rect game = Placed(rig.Host.View);

        rig.Overlay.FreeCamera.Detach();

        Assert.NotNull(rig.Host.ViewCamera);
        Assert.Equal(game, Placed(rig.Host.View));
    }

    [Fact]
    public void ARunWhoseFreeCameraMoved_StepsAsARunNeverOpened()
    {
        FramedScene touchedScene = new();
        FramedScene untouchedScene = new();
        using OverlayRig touched = Framing(touchedScene);
        using OverlayRig untouched = Framing(untouchedScene);

        touched.Frame();
        touched.Open();
        touched.PlaceWorld();
        touched.Frame(DeviceSnapshot.Empty.WithPointer(WorldPoint).With(MouseButton.Middle));
        touched.Frame(DeviceSnapshot.Empty.WithPointer(WorldPoint + new Vector2(40f, 25f)).With(MouseButton.Middle));
        touched.PlaceWorld();
        touched.Frame(DeviceSnapshot.Empty.WithPointer(WorldPoint).WithScroll(new Vector2(0f, 2f)));
        touched.Press(Key.Right);

        Assert.True(touched.Overlay.FreeCamera.Detached);
        Assert.Equal(2, touched.Scheduler.Tick);

        touched.Press(Key.Grave);
        for (int frame = 0; frame < 4; frame++)
        {
            touched.Frame();
        }

        while (untouched.Scheduler.Tick < touched.Scheduler.Tick)
        {
            untouched.Frame();
        }

        Camera camera = touchedScene.Camera;
        Camera reference = untouchedScene.Camera;
        Assert.Null(touched.Host.ViewCamera);
        Assert.Equal(untouched.Host.View.Camera, touched.Host.View.Camera);
        Assert.Equal(
            (reference.Center, reference.Zoom, reference.VisibleRegion, reference.CanvasToWorld(Vector2.Zero)),
            (camera.Center, camera.Zoom, camera.VisibleRegion, camera.CanvasToWorld(Vector2.Zero)));
        Assert.Equal(untouchedScene.Walker.Position, touchedScene.Walker.Position);
    }

    // The restarted scene's simulation has no stand-in. The next pan seeds from the new scene's camera,
    // and the readout holds no point from the scene before.
    [Fact]
    public void ARestartWhileDetached_ReturnsTheViewToTheNewScenesCamera()
    {
        List<FramedScene> scenes = [];
        using OverlayRig rig = new(new SceneHost(
            SceneTransition.ToScene(typeof(FramedScene), null),
            (in SceneTransition _) =>
            {
                scenes.Add(new FramedScene());

                return scenes[^1];
            },
            new Run()));
        rig.Scheduler.Output = Output;
        rig.Open();
        rig.PlaceWorld();
        Drag(rig, new Vector2(40f, 25f));
        Assert.True(rig.Overlay.FreeCamera.Detached);
        Assert.NotEmpty(rig.Overlay.Scene.Readout()[2]);

        rig.Press(Key.R);

        Assert.Equal(2, scenes.Count);
        Assert.Same(scenes[1], rig.Host.Scene);
        Assert.Null(rig.Host.ViewCamera);
        Assert.False(rig.Overlay.FreeCamera.Detached);
        Assert.Empty(rig.Overlay.Scene.Readout()[2]);

        rig.PlaceWorld();
        Rect game = Placed(rig.Host.View);
        Vector2 travel = new(10f, 0f);
        Drag(rig, travel);

        Vector2 expected = ((new Vector2(game.Left, game.Top) + new Vector2(game.Right, game.Bottom)) / 2f)
            - (travel / rig.World!.Value.PixelsPerUnit);
        CameraView free = rig.Host.ViewCamera!.Value;
        Assert.Equal(rig.Host.Scene.Camera.ViewportSize, free.Size);
        Assert.Equal(expected.X, free.Center.X, 3);
        Assert.Equal(expected.Y, free.Center.Y, 3);
    }

    // A press held across a close and a reopen is not a pan, however far the pointer moved meanwhile.
    [Fact]
    public void AMiddlePressHeldAcrossACloseAndReopen_DoesNotPan()
    {
        using OverlayRig rig = Framing(new FramedScene());
        rig.Open();
        rig.PlaceWorld();
        DeviceSnapshot held = DeviceSnapshot.Empty.With(MouseButton.Middle);

        rig.Frame(held.WithPointer(WorldPoint));
        rig.Frame(held.WithPointer(WorldPoint).With(Key.Grave));
        rig.Frame(held.WithPointer(WorldPoint + new Vector2(60f, 0f)));
        rig.Frame(held.WithPointer(WorldPoint + new Vector2(60f, 0f)).With(Key.Grave));
        rig.Frame(held.WithPointer(WorldPoint + new Vector2(90f, 0f)));

        Assert.True(rig.Overlay.IsOpen);
        Assert.Null(rig.Host.ViewCamera);
    }

    [Fact]
    public void ASpaceLeftPan_EndsWhenSpaceIsReleased()
    {
        using OverlayRig rig = Framing(new FramedScene());
        rig.Open();
        rig.PlaceWorld();
        DeviceSnapshot both = DeviceSnapshot.Of(Key.Space).With(MouseButton.Left);

        rig.Frame(both.WithPointer(WorldPoint));
        rig.Frame(both.WithPointer(WorldPoint + new Vector2(20f, 0f)));
        Vector2 panned = rig.Host.ViewCamera!.Value.Center;
        rig.Frame(DeviceSnapshot.Empty.With(MouseButton.Left).WithPointer(WorldPoint + new Vector2(20f, 0f)));
        rig.Frame(DeviceSnapshot.Empty.With(MouseButton.Left).WithPointer(WorldPoint + new Vector2(80f, 0f)));

        Assert.Equal(panned, rig.Host.ViewCamera!.Value.Center);
    }

    // The menu is withdrawn, and the key still reaches the free camera.
    [Fact]
    public void TheGameCameraKey_ReattachesWhileHidden()
    {
        using OverlayRig rig = Framing(new FramedScene());
        rig.Open();
        rig.PlaceWorld();
        Drag(rig, new Vector2(30f, 0f));
        rig.Press(Key.H);
        Assert.True(rig.Overlay.IsHidden);
        Assert.NotNull(rig.Host.ViewCamera);

        rig.Press(Key.C);

        Assert.True(rig.Overlay.IsHidden);
        Assert.Null(rig.Host.ViewCamera);
        Assert.False(rig.Overlay.FreeCamera.Detached);
    }

    // Over the world a notch up scrolls a tenth of the view up, Shift turns it left, and Ctrl zooms in by
    // one step about the pointer. Neither the wheel nor a modifier reaches the game.
    [Fact]
    public void TheWheel_ScrollsTheRowsOverTheMenuAndScrollsOrZoomsTheViewOverTheWorld()
    {
        using OverlayRig rig = Framing(new FramedScene(), LongRegistry());
        rig.Open();
        rig.Press(Key.L);
        rig.PlaceWorld();
        string[] labels = rig.Rows();
        DeviceSnapshot up = DeviceSnapshot.Empty.WithPointer(WorldPoint).WithScroll(new Vector2(0f, 1f));

        DeviceSnapshot game = rig.Frame(DeviceSnapshot.Of(Key.LeftControl).WithPointer(new Vector2(6f, 40f)).WithScroll(new Vector2(0f, -1f)));

        Assert.Equal(labels[3], rig.Overlay.Scene.ShownRows()[0]);
        Assert.Null(rig.Host.ViewCamera);
        Assert.Equal(Vector2.Zero, game.Scroll);

        Letterbox fit = rig.World!.Value.Fit;
        Vector2 visible = new Vector2(fit.Width, fit.Height) / fit.Scale;
        rig.Overlay.FreeCamera.Detach();
        Vector2 center = rig.Host.ViewCamera!.Value.Center;
        rig.Frame(up);
        rig.PlaceWorld();
        center.Y -= visible.Y * FreeCamera.ScrollStep;
        AssertCenter(center, rig);

        game = rig.Frame(up.With(Key.LeftShift));
        rig.PlaceWorld();
        center.X -= visible.X * FreeCamera.ScrollStep;
        AssertCenter(center, rig);
        Assert.False(game.IsDown(Key.LeftShift));

        Vector2 under = rig.World!.Value.ToWorld(WorldPoint);
        Vector2 size = rig.Host.View.Camera.Size;
        game = rig.Frame(up.With(Key.RightControl));
        rig.PlaceWorld();

        Assert.Equal(labels[3], rig.Overlay.Scene.ShownRows()[0]);
        Assert.Equal(Vector2.Zero, game.Scroll);
        Assert.Equal(size / FreeCamera.ZoomStep, rig.Host.View.Camera.Size);
        Vector2 still = rig.World!.Value.ToWorld(WorldPoint);
        Assert.Equal(under.X, still.X, 3);
        Assert.Equal(under.Y, still.Y, 3);
    }

    private static void AssertCenter(Vector2 expected, OverlayRig rig)
    {
        Vector2 center = rig.Host.ViewCamera!.Value.Center;
        Assert.Equal(expected.X, center.X, 3);
        Assert.Equal(expected.Y, center.Y, 3);
    }

    // A middle-button drag from the world point, released after it.
    private static void Drag(OverlayRig rig, Vector2 travel)
    {
        rig.Frame(DeviceSnapshot.Empty.WithPointer(WorldPoint).With(MouseButton.Middle));
        rig.Frame(DeviceSnapshot.Empty.WithPointer(WorldPoint + travel).With(MouseButton.Middle));
        rig.Frame(DeviceSnapshot.Empty.WithPointer(WorldPoint + travel));
    }

    // The renderer's placement of a frame's world on the rig's output.
    private static Rect Placed(FrameView view)
    {
        CameraView camera = view.Camera.At(1f);
        ScreenLayout layout = FrameLayout.Layout(null, camera, view.Canvas, (int)Output.X, (int)Output.Y);

        return camera.Place(1f, layout.Span);
    }

    // A Load Scene page longer than the menu's window.
    private static SceneRegistry LongRegistry()
    {
        Type[] types = typeof(object).Assembly.GetExportedTypes();
        List<SceneRegistration> registrations = [];
        for (int index = 0; index < OverlayScene.MaxRows + 4; index++)
        {
            registrations.Add(SceneRegistration.Plain(types[index], static _ => new PlainScene()));
        }

        return new SceneRegistry(new EntityRegistry([]), registrations);
    }
}
