using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.DevTools;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using static Capsule.Tests.Runtime.OverlayFixtures;

namespace Capsule.Tests.Runtime;

// The pointer over the rows, and the wheel and scrollbar over a page longer than the window.
public sealed class OverlayScrollTests
{
    // The game's pointer is in its canvas, letterboxed and scaled into the window. The overlay's
    // canvas is the window at its own integer scale.
    [Fact]
    public void APointerOverARow_FocusesItThroughTheGamesPlacementAndLeavesTheGamesPointerAlone()
    {
        using OverlayRig rig = new(CreateHost());
        DebugOverlay overlay = rig.Overlay;
        ScreenPlacement gameLayer = new(new Vector2(100f, 20f), 3f);
        const int overlayScale = 2;
        rig.Open();

        // The second row's line in overlay canvas pixels, then that point in the window, then the
        // game canvas position the host would have sampled for it.
        Vector2 window = RowPoint(1) * overlayScale;
        Vector2 gamePoint = (window - gameLayer.Origin) / gameLayer.Scale;

        DeviceSnapshot game = overlay.Intercept(DeviceSnapshot.Empty.WithPointer(gamePoint), gameLayer, overlayScale);
        rig.Scheduler.Advance(OverlayRig.StepSeconds, game, rig.Host);
        overlay.Update();

        Assert.Equal(gamePoint, game.Pointer);
        Assert.Equal("Step", rig.Focused());

        overlay.Intercept(DeviceSnapshot.Empty.WithPointer(gamePoint).With(MouseButton.Left), gameLayer, overlayScale);
        rig.Scheduler.Advance(OverlayRig.StepSeconds, DeviceSnapshot.Empty, rig.Host);
        overlay.Update();

        Assert.Equal(1, rig.Scheduler.Tick);
    }

    // Up/Dn would otherwise be overwritten the next frame by the row still under an unmoved pointer.
    [Fact]
    public void APointerRestingOnARow_DoesNotOverwriteTheFocusTheKeysMoved()
    {
        using OverlayRig rig = new(CreateHost());
        Vector2 firstRow = RowPoint(0);

        rig.Frame(DeviceSnapshot.Of(Key.Grave).WithPointer(firstRow));
        rig.Frame(DeviceSnapshot.Empty.WithPointer(firstRow));
        Assert.Equal(0, rig.Overlay.Focus);

        rig.Frame(DeviceSnapshot.Of(Key.Down).WithPointer(firstRow));
        rig.Frame(DeviceSnapshot.Empty.WithPointer(firstRow));
        Assert.Equal(1, rig.Overlay.Focus);

        // Moving the pointer, even within the same row, takes the focus back.
        rig.Frame(DeviceSnapshot.Empty.WithPointer(firstRow + new Vector2(1f, 0f)));
        Assert.Equal(0, rig.Overlay.Focus);
    }

    // A negative notch is the wheel turned toward the user. A direction press afterwards snaps the
    // window back to show the focus.
    [Fact]
    public void AWheelNotchDown_MovesTheWindowThreeRowsThenMenuDownSnapsItBackToTheFocus()
    {
        using OverlayRig rig = OpenLongPage(OverlayScene.MaxRows + 4);
        string[] labels = rig.Rows();

        Assert.Equal(labels[0], FirstShown(rig));

        rig.Frame(DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -1f)));

        Assert.Equal(0, rig.Overlay.Focus);
        Assert.Equal(labels[3], FirstShown(rig));

        rig.Press(Key.Down);

        Assert.Equal(1, rig.Overlay.Focus);
        Assert.Equal(labels[1], FirstShown(rig));
    }

    // A sixth of a notch is half a row. The remainder left as the overlay closes is discarded.
    [Fact]
    public void TheScrollRemainder_CarriesAcrossFramesAndIsDiscardedWhenTheOverlayCloses()
    {
        using OverlayRig rig = OpenLongPage(OverlayScene.MaxRows + 4);
        string[] labels = rig.Rows();
        DeviceSnapshot sixth = DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -1f / 6f));

        rig.Frame(sixth);

        Assert.Equal(labels[0], FirstShown(rig));

        rig.Frame(sixth);

        Assert.Equal(labels[1], FirstShown(rig));

        rig.Frame(sixth);
        rig.Press(Key.Grave);
        rig.Open();
        rig.Frame(sixth);

        Assert.Equal(labels[1], FirstShown(rig));
    }

    [Fact]
    public void TheScrollbarsThumb_SitsInsideTheTrackShrinksReachesItsEndAndIsEmptyWhenThePageFits()
    {
        (Rect Track, Rect Thumb) shorter = Scrollbar(OverlayScene.MaxRows + 2, toLastRow: false);
        (Rect Track, Rect Thumb) longer = Scrollbar(OverlayScene.MaxRows + 20, toLastRow: false);
        (Rect Track, Rect Thumb) atEnd = Scrollbar(OverlayScene.MaxRows + 2, toLastRow: true);
        (Rect Track, Rect Thumb) fitting = Scrollbar(OverlayScene.MaxRows, toLastRow: false);

        Assert.True(shorter.Thumb.Left >= shorter.Track.Left && shorter.Thumb.Right <= shorter.Track.Right);
        Assert.True(shorter.Thumb.Top >= shorter.Track.Top && shorter.Thumb.Bottom <= shorter.Track.Bottom);
        Assert.True(longer.Thumb.Size.Y < shorter.Thumb.Size.Y);
        Assert.Equal(atEnd.Track.Bottom, atEnd.Thumb.Bottom);
        Assert.True(fitting.Track.IsEmpty);
        Assert.True(fitting.Thumb.IsEmpty);
    }

    // The middle of a root row on the overlay's canvas, under the readout and the blank row.
    private static Vector2 RowPoint(int row)
    {
        float lineHeight = BitmapFont.Default.LineHeight;

        return new Vector2(6f, 4f + ((2f + row) * lineHeight) + (lineHeight / 2f));
    }

    private static string FirstShown(OverlayRig rig) => rig.Overlay.Scene.ShownRows()[0];

    // An open Load Scene page of rowCount distinct classes borrowed from the runtime.
    private static OverlayRig OpenLongPage(int rowCount)
    {
        Type[] types = typeof(object).Assembly.GetExportedTypes();
        List<SceneRegistration> registrations = [];
        for (int index = 0; index < rowCount; index++)
        {
            registrations.Add(SceneRegistration.Plain(types[index], static _ => new PlainScene()));
        }

        OverlayRig rig = new(CreateHost(), new SceneRegistry(new EntityRegistry([]), registrations));
        rig.Open();
        rig.Press(Key.L);

        return rig;
    }

    private static (Rect Track, Rect Thumb) Scrollbar(int rowCount, bool toLastRow)
    {
        using OverlayRig rig = OpenLongPage(rowCount);
        for (int row = 0; toLastRow && row < rowCount - 1; row++)
        {
            rig.Press(Key.Down);
        }

        return (rig.Overlay.Scene.ScrollTrack, rig.Overlay.Scene.ScrollThumb);
    }
}
