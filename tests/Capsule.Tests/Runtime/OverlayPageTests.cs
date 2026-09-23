using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using static Capsule.Tests.Runtime.OverlayFixtures;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// What the overlay's pages hold, how the focus moves over them, and how one page opens another.
public sealed class OverlayPageTests
{
    [Fact]
    public void TheRootPage_ListsTheEngineRowsForARunOfScenes()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());

        Open(overlay, scheduler, host);

        Assert.Equal(
            ["Scene", "Step", "Debug Draw", "Time Scale", "Restart", "Load Scene", "Frame Pane", "Hide", "Exit"],
            Rows(overlay));
        Assert.Equal(0, overlay.Focus);
        Assert.Null(overlay.Title);
    }

    // Without a run of scenes there is no scene page, restart, load or exit to offer.
    [Fact]
    public void TheRootPage_OffersOnlyTheHostRowsWithoutARunOfScenes()
    {
        FixedStepScheduler scheduler = CreateScheduler();
        RecordingSimulation simulation = new();
        using OverlayHost overlay = new(Key.Grave, scheduler, simulation);

        Open(overlay, scheduler, simulation);

        Assert.Equal(["Step", "Debug Draw", "Time Scale", "Frame Pane", "Hide"], Rows(overlay));
    }

    // A row's hotkey is drawn in a column past the widest label of the page, so the keys line up.
    [Fact]
    public void ARowWithAHotkey_DrawsItInASharedColumn()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        string[] shown = overlay.Scene.ShownRows();

        Assert.Equal(overlay.Rows.Count, shown.Length);
        Assert.Equal("Scene       S", shown[0]);
        Assert.Equal("Debug Draw  D", shown[2]);
    }

    // An opener's hotkey is the root page's alone: inside a submenu it changes nothing, not even the
    // status line Debug Draw would otherwise write about a scene that has emitted nothing. Exit acts
    // on the run from any depth.
    [Fact]
    public void TheHotkeys_OpenPagesOnlyFromTheRootAndExitThroughTheRunAtAnyDepth()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.L);

        Assert.Equal("Load Scene", overlay.Title);
        Assert.Equal(2, overlay.Depth);

        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.D);
        Press(overlay, scheduler, host, Key.T);

        Assert.Equal("Load Scene", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        Assert.Equal(string.Empty, overlay.Status);

        Press(overlay, scheduler, host, Key.E);

        Assert.True(host.ExitRequested);
        Assert.Equal(1, scheduler.Tick);
    }

    [Fact]
    public void NavigatingDownAndEnter_ActivatesTheFocusedRow()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Assert.Equal(0, overlay.Focus);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Down));

        Assert.Equal(1, overlay.Focus);
        Assert.Equal("Step", Focused(overlay));
        Assert.Equal(0, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter));

        Assert.Equal(1, scheduler.Tick);
        Assert.True(scheduler.Held);
        Assert.True(overlay.IsOpen);
    }

    // Up from the first row wraps to the last.
    [Fact]
    public void TheFocus_WrapsAtEitherEnd()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.Up);

        Assert.Equal("Exit", Focused(overlay));

        Press(overlay, scheduler, host, Key.Down);

        Assert.Equal(0, overlay.Focus);
    }

    // The game's pointer is in its canvas, letterboxed and scaled into the window; the overlay's
    // canvas is the window at its own integer scale. A pointer over the second row in window terms
    // must focus that row, and the game must still see the pointer it was handed.
    [Fact]
    public void APointerOverARow_FocusesItThroughTheGamesPlacementAndLeavesTheGamesPointerAlone()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        ScreenPlacement gameLayer = new(new Vector2(100f, 20f), 3f);
        const int overlayScale = 2;

        overlay.Observe(DeviceSnapshot.Of(Key.Grave), gameLayer, overlayScale);
        scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host);
        overlay.Step();
        Assert.True(overlay.IsOpen);

        // The second row's line in overlay canvas pixels, then that point in the window, then the
        // game canvas position the host would have sampled for it.
        float lineHeight = BitmapFont.Default.LineHeight;
        Vector2 overlayPoint = new(6f, 4f + (3f * lineHeight) + (lineHeight / 2f));
        Vector2 window = overlayPoint * overlayScale;
        Vector2 gamePoint = (window - gameLayer.Origin) / gameLayer.Scale;

        DeviceSnapshot game = overlay.Observe(DeviceSnapshot.Empty.WithPointer(gamePoint), gameLayer, overlayScale);
        scheduler.Advance(StepSeconds, game, host);
        overlay.Step();

        Assert.Equal(gamePoint, game.Pointer);
        Assert.Equal(1, overlay.Focus);
        Assert.Equal("Step", Focused(overlay));

        // A click on the row the pointer rests on activates it.
        overlay.Observe(DeviceSnapshot.Empty.WithPointer(gamePoint).With(MouseButton.Left), gameLayer, overlayScale);
        scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host);
        overlay.Step();

        Assert.Equal(1, scheduler.Tick);
    }

    // The pointer takes the focus only by moving onto a row, not by resting on one the keys just
    // moved off of: Up/Dn would otherwise be overwritten the very next frame by the row still under
    // an unmoved pointer.
    [Fact]
    public void APointerRestingOnARow_DoesNotOverwriteTheFocusTheKeysMoved()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        float lineHeight = BitmapFont.Default.LineHeight;
        Vector2 firstRow = new(6f, 4f + (2f * lineHeight) + (lineHeight / 2f));

        overlay.Observe(DeviceSnapshot.Of(Key.Grave).WithPointer(firstRow));
        scheduler.Advance(StepSeconds, DeviceSnapshot.Empty, host);
        overlay.Step();
        Assert.True(overlay.IsOpen);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty.WithPointer(firstRow));
        Assert.Equal(0, overlay.Focus);

        // The key moves the focus while the pointer keeps resting on the row it started on.
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Down).WithPointer(firstRow));
        Assert.Equal(1, overlay.Focus);

        // The pointer still has not moved, so it does not claim the focus back on the frame after.
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty.WithPointer(firstRow));
        Assert.Equal(1, overlay.Focus);

        // Moving the pointer, even within the same row, takes the focus back.
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty.WithPointer(firstRow + new Vector2(1f, 0f)));
        Assert.Equal(0, overlay.Focus);
    }

    [Fact]
    public void Back_PopsTheSubmenuAndReturnsTheFocusToTheRowThatOpenedIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());

        Open(overlay, scheduler, host);
        for (int row = 0; row < 5; row++)
        {
            Press(overlay, scheduler, host, Key.Down);
        }

        Assert.Equal("Load Scene", Focused(overlay));

        Press(overlay, scheduler, host, Key.Enter);
        Press(overlay, scheduler, host, Key.Down);

        Assert.Equal("Load Scene", overlay.Title);
        Assert.Equal(1, overlay.Focus);

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal(1, overlay.Depth);
        Assert.Null(overlay.Title);
        Assert.Equal("Load Scene", Focused(overlay));

        Press(overlay, scheduler, host, Key.Left);

        Assert.Equal(1, overlay.Depth);
        Assert.Equal(0, scheduler.Tick);
    }

    [Fact]
    public void TheLoadScenePage_ListsTheRegisteredClassesSortedAndRequestsEachByItsRegisteredForm()
    {
        List<SceneTransition> resolved = [];
        using SceneHost host = CreateHost(resolved: resolved);
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.L);

        Assert.Equal(["NamedScene", "PayloadScene", "PlainScene"], Rows(overlay));
        Assert.Equal("Load Scene", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        Assert.Equal(0, overlay.Focus);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter));

        Assert.IsType<NamedScene>(host.Scene);
        Assert.Equal(2, resolved.Count);
        SceneTransition named = resolved[^1];
        Assert.Equal(SceneTransitionKind.Named, named.Kind);
        Assert.Equal(NamedDocument, named.DocumentName);
        Assert.Null(named.Payload);
        Assert.Equal(1, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        Assert.Equal(2, overlay.Depth);
        Assert.Equal("NamedScene  tick 1", overlay.Readout);

        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.IsType<PlainScene>(host.Scene);
        Assert.Equal(SceneTransitionKind.Scene, resolved[^1].Kind);
        Assert.Equal(typeof(PlainScene), resolved[^1].SceneType);
        Assert.Equal(2, overlay.Depth);
    }

    [Fact]
    public void TheTimeScalePage_MarksThePaceInForceSetsItWithoutATickAndKeepsItForThePlaySession()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.T);

        Assert.Equal("Time Scale", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        AssertLadder(overlay, host.Run.TimeScale);

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        // The pick lands on the run, where the game reads it, and on the applied value; the marks
        // follow at once, the focus and depth stay, and no tick is stepped for it.
        Assert.Equal(0.5, host.Run.TimeScale);
        Assert.Equal(0.5, scheduler.TimeScale);
        AssertLadder(overlay, host.Run.TimeScale);
        Assert.Equal(1, overlay.Focus);
        Assert.Equal(2, overlay.Depth);
        Assert.Equal(0, scheduler.Tick);

        // Closed, the game runs at the chosen pace: a frame worth one step buys half of one.
        Press(overlay, scheduler, host, Key.Backspace);
        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));

        Assert.False(overlay.IsOpen);
        Assert.Equal(0, scheduler.Tick);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        Assert.Equal(1, scheduler.Tick);

        // Reopened, hidden, shown again and across a Restart, the overlay never resets it.
        Press(overlay, scheduler, host, Key.Grave);
        Press(overlay, scheduler, host, Key.H);
        Press(overlay, scheduler, host, Key.H);
        Assert.True(overlay.IsOpen);
        Press(overlay, scheduler, host, Key.R);
        Assert.IsType<ReadoutScene>(host.Scene);

        Press(overlay, scheduler, host, Key.T);

        Assert.Equal(0.5, host.Run.TimeScale);
        AssertLadder(overlay, host.Run.TimeScale);
    }

    // The run holds the pace, so the ladder shows what the game set, and marks nothing where the game
    // set a pace the ladder does not offer.
    [Fact]
    public void TheTimeScalePage_MarksThePaceTheGameSetAndNoRowForOneOffTheLadder()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        host.Run.TimeScale = 2;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.T);

        AssertLadder(overlay, host.Run.TimeScale);

        // Set off the ladder by a hand outside the overlay, bypassing SetTimeScale: the page holds
        // its last marks on an idle frame, since nothing here told it the run had changed, and
        // catches up the next time a host act rebuilds it, here by leaving and reopening the page.
        host.Run.TimeScale = 1.5;
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        AssertLadder(overlay, 2);

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.T);

        AssertLadder(overlay, 1.5);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(0.25, host.Run.TimeScale);
        AssertLadder(overlay, host.Run.TimeScale);
    }

    [Fact]
    public void TheDebugDrawPage_SaysSoWhileNothingHasEmitted()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.D);

        Assert.Equal("Debug Draw", overlay.Title);
        Assert.Equal(["<No channel has emitted yet>"], Rows(overlay));
        Assert.Empty(overlay.Channels);
    }

    [Fact]
    public void TheReadout_NamesTheCurrentSceneAndTick()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);

        Assert.Equal("ReadoutScene  tick 0", overlay.Readout);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal("ReadoutScene  tick 1", overlay.Readout);
    }

    // The overlay's canvas is the back buffer at an integer scale, one step per doubling of height.
    [Theory]
    [InlineData(720, 1)]
    [InlineData(1079, 1)]
    [InlineData(1080, 2)]
    [InlineData(2159, 2)]
    [InlineData(2160, 3)]
    public void TheOverlaysScale_StepsWithTheBackBuffersHeight(int height, int scale) =>
        Assert.Equal(scale, OverlayHost.ScaleFor(height));

    // ScrollY reads positive away from the user (MouseAxis.ScrollY), so a negative notch is the wheel
    // turned toward the user: scrolling down. The wheel moves the window, three rows a notch, and
    // leaves the focus alone; a direction press afterwards is what brings the window back to the
    // focus, snapping it to show the row that press just moved to.
    [Fact]
    public void AWheelNotchDown_MovesTheWindowThreeRowsThenMenuDownSnapsItBackToTheFocus()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(
            Key.Grave,
            scheduler,
            host,
            host,
            registry: LongLoadSceneRegistry(OverlayScene.MaxRows + 4));

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.L);

        Assert.Equal("Load Scene", overlay.Title);
        Assert.True(overlay.Rows.Count > OverlayScene.MaxRows);

        string[] labels = Rows(overlay);

        Assert.Equal(0, overlay.Focus);
        Assert.Equal(labels[0], overlay.Scene.ShownRows()[0]);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -1f)));

        Assert.Equal(0, overlay.Focus);
        Assert.Equal(labels[3], overlay.Scene.ShownRows()[0]);

        Press(overlay, scheduler, host, Key.Down);

        Assert.Equal(1, overlay.Focus);
        Assert.Equal(labels[1], overlay.Scene.ShownRows()[0]);
    }

    [Fact]
    public void TheScrollbarsThumb_SitsInsideTheTrackShrinksReachesItsEndAndIsEmptyWhenThePageFits()
    {
        (Rect Track, Rect Thumb) shorter = ScrollbarForALoadScenePageOf(OverlayScene.MaxRows + 2);
        (Rect Track, Rect Thumb) longer = ScrollbarForALoadScenePageOf(OverlayScene.MaxRows + 20);

        Assert.True(shorter.Thumb.Left >= shorter.Track.Left && shorter.Thumb.Right <= shorter.Track.Right);
        Assert.True(shorter.Thumb.Top >= shorter.Track.Top && shorter.Thumb.Bottom <= shorter.Track.Bottom);
        Assert.True(longer.Thumb.Size.Y < shorter.Thumb.Size.Y);

        (Rect Track, Rect Thumb) atEnd = ScrollbarAtTheLastRowOfALoadScenePageOf(OverlayScene.MaxRows + 2);
        Assert.Equal(atEnd.Track.Bottom, atEnd.Thumb.Bottom);

        (Rect Track, Rect Thumb) fitting = ScrollbarForALoadScenePageOf(OverlayScene.MaxRows);
        Assert.True(fitting.Track.IsEmpty);
        Assert.True(fitting.Thumb.IsEmpty);
    }

    // Two frames of a sixth of a notch each (half a row, since a whole notch moves three) carry the
    // fraction that does not yet make a whole row: the window moves only on the second frame, where
    // the halves add to one. A sixth left over when the overlay closes is discarded, not carried into
    // the next time it opens.
    [Fact]
    public void TheScrollRemainder_CarriesAcrossFramesAndIsDiscardedWhenTheOverlayCloses()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(
            Key.Grave,
            scheduler,
            host,
            host,
            registry: LongLoadSceneRegistry(OverlayScene.MaxRows + 4));

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.L);

        string[] labels = Rows(overlay);

        Assert.Equal(labels[0], overlay.Scene.ShownRows()[0]);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -1f / 6f)));

        Assert.Equal(labels[0], overlay.Scene.ShownRows()[0]);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -1f / 6f)));

        Assert.Equal(labels[1], overlay.Scene.ShownRows()[0]);

        // A sixth of a notch left unfinished as the overlay closes.
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -1f / 6f)));

        Assert.Equal(labels[1], overlay.Scene.ShownRows()[0]);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Grave));

        Assert.False(overlay.IsOpen);

        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Open(overlay, scheduler, host);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty.WithScroll(new Vector2(0f, -1f / 6f)));

        Assert.Equal(labels[1], overlay.Scene.ShownRows()[0]);
    }

    // A registry of rowCount plain scenes, distinct classes borrowed from the runtime so the page lists
    // more rows than the window shows.
    private static SceneRegistry LongLoadSceneRegistry(int rowCount)
    {
        Type[] types = typeof(object).Assembly.GetExportedTypes();
        List<SceneRegistration> registrations = [];
        for (int index = 0; index < rowCount; index++)
        {
            registrations.Add(SceneRegistration.Plain(types[index], static _ => new PlainScene()));
        }

        return new SceneRegistry(new EntityRegistry([]), registrations);
    }

    // Opens a Load Scene page of rowCount rows and reads its scrollbar back.
    private static (Rect Track, Rect Thumb) ScrollbarForALoadScenePageOf(int rowCount)
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: LongLoadSceneRegistry(rowCount));

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.L);

        return (overlay.Scene.ScrollTrack, overlay.Scene.ScrollThumb);
    }

    // Opens a Load Scene page of rowCount rows, walks the focus down to its last row, and reads the
    // scrollbar back.
    private static (Rect Track, Rect Thumb) ScrollbarAtTheLastRowOfALoadScenePageOf(int rowCount)
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: LongLoadSceneRegistry(rowCount));

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.L);

        for (int row = 0; row < rowCount - 1; row++)
        {
            Press(overlay, scheduler, host, Key.Down);
        }

        return (overlay.Scene.ScrollTrack, overlay.Scene.ScrollThumb);
    }

    // One row per pace on the host's ladder, in its order, and a mark on exactly the row whose pace is
    // the one in force: none, where the game set one off the ladder.
    private static void AssertLadder(OverlayHost overlay, double pace)
    {
        string[] labels = Rows(overlay);

        Assert.Equal(OverlayHost.TimeScales.Length, labels.Length);
        for (int index = 0; index < labels.Length; index++)
        {
            (double scale, string label) = OverlayHost.TimeScales[index];

            Assert.EndsWith(label, labels[index], StringComparison.Ordinal);
            Assert.Equal(scale == pace, labels[index].StartsWith("(x)", StringComparison.Ordinal));
        }
    }
}
