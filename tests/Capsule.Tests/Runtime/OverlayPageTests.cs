using Capsule.Input;
using Capsule.Runtime.DevTools;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using static Capsule.Tests.Runtime.OverlayFixtures;

namespace Capsule.Tests.Runtime;

// What the overlay's pages hold, how the focus moves over them, and how one page opens another.
public sealed class OverlayPageTests
{
    private const string UnclaimedDocument = "attic";

    [Fact]
    public void TheRootPage_ListsTheEngineRowsForARunOfScenes()
    {
        using OverlayRig rig = new(CreateHost(), CreateRegistry());

        rig.Open();

        Assert.Equal(
            ["Scene", "Step", "Debug Draw", "Time Scale", "Restart", "Load Scene", "Frame Pane", "Hide", "Exit"],
            rig.Rows());
        Assert.Equal(0, rig.Overlay.Focus);
        Assert.Null(rig.Overlay.Title);
    }

    [Fact]
    public void TheRootPage_OffersOnlyTheHostRowsWithoutARunOfScenes()
    {
        using OverlayRig rig = new();

        rig.Open();

        Assert.Equal(["Step", "Debug Draw", "Time Scale", "Frame Pane", "Hide"], rig.Rows());
    }

    [Fact]
    public void ARowWithAHotkey_DrawsItInASharedColumn()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        string[] shown = rig.Overlay.Scene.ShownRows();

        Assert.Equal(rig.Overlay.Rows.Count, shown.Length);
        Assert.Equal("Scene       S", shown[0]);
        Assert.Equal("Debug Draw  D", shown[2]);
    }

    // Inside a submenu an opener's hotkey changes nothing, not even the status line. Exit acts on the
    // run from any depth.
    [Fact]
    public void TheHotkeys_OpenPagesOnlyFromTheRootAndExitThroughTheRunAtAnyDepth()
    {
        using OverlayRig rig = new(CreateHost(), CreateRegistry());

        rig.Open();
        rig.Press(Key.L);
        rig.Press(Key.L);
        rig.Press(Key.D);
        rig.Press(Key.T);

        Assert.Equal("Load Scene", rig.Overlay.Title);
        Assert.Equal(2, rig.Overlay.Depth);
        Assert.Equal(string.Empty, rig.Overlay.Status);

        rig.Press(Key.E);

        Assert.True(rig.Host.ExitRequested);
        Assert.Equal(1, rig.Scheduler.Tick);
    }

    [Fact]
    public void TheFocus_WrapsAtEitherEnd()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Press(Key.Up);

        Assert.Equal("Exit", rig.Focused());

        rig.Press(Key.Down);

        Assert.Equal(0, rig.Overlay.Focus);
    }

    [Fact]
    public void Back_PopsTheSubmenuAndReturnsTheFocusToTheRowThatOpenedIt()
    {
        using OverlayRig rig = new(CreateHost(), CreateRegistry());

        rig.Open();
        for (int row = 0; row < 5; row++)
        {
            rig.Press(Key.Down);
        }

        Assert.Equal("Load Scene", rig.Focused());

        rig.Press(Key.Enter);
        rig.Press(Key.Down);

        Assert.Equal("Load Scene", rig.Overlay.Title);
        Assert.Equal(1, rig.Overlay.Focus);

        rig.Press(Key.Backspace);

        Assert.Equal(1, rig.Overlay.Depth);
        Assert.Null(rig.Overlay.Title);
        Assert.Equal("Load Scene", rig.Focused());

        rig.Press(Key.Left);

        Assert.Equal(1, rig.Overlay.Depth);
        Assert.Equal(0, rig.Scheduler.Tick);
    }

    // A document, claimed by a class or not, loads by name. A plain class loads by type.
    [Fact]
    public void TheLoadScenePage_ListsEveryRegistrationSortedAndRequestsEachByItsRegisteredForm()
    {
        List<SceneTransition> resolved = [];
        using OverlayRig rig = new(
            CreateHost(resolved: resolved),
            new SceneRegistry(new EntityRegistry([]), [.. CreateRegistry().Registrations, Unclaimed()]));

        rig.Open();
        rig.Press(Key.L);

        Assert.Equal([UnclaimedDocument, "NamedScene", "PayloadScene", "PlainScene"], rig.Rows());
        Assert.Equal("Load Scene", rig.Overlay.Title);

        rig.Frame(DeviceSnapshot.Of(Key.Enter));

        Assert.IsType<NamedScene>(rig.Host.Scene);
        Assert.Equal(SceneTransitionKind.Named, resolved[^1].Kind);
        Assert.Equal(UnclaimedDocument, resolved[^1].DocumentName);
        Assert.Null(resolved[^1].Payload);
        Assert.Equal(1, rig.Scheduler.Tick);

        rig.Frame();

        Assert.Equal(2, rig.Overlay.Depth);
        Assert.Equal("NamedScene  tick 1", rig.Overlay.Readout);

        rig.Press(Key.Up);
        rig.Press(Key.Enter);

        Assert.IsType<PlainScene>(rig.Host.Scene);
        Assert.Equal(SceneTransitionKind.Scene, resolved[^1].Kind);
        Assert.Equal(typeof(PlainScene), resolved[^1].SceneType);
    }

    [Fact]
    public void TheLoadScenePage_IsOfferedToAGameWhoseOnlyRegistrationIsAShippedDocument()
    {
        using OverlayRig rig = new(CreateHost(), new SceneRegistry(new EntityRegistry([]), [Unclaimed()]));

        rig.Open();
        rig.Press(Key.L);

        Assert.Equal([UnclaimedDocument], rig.Rows());
    }

    [Fact]
    public void TheTimeScalePage_MarksThePaceInForceSetsItWithoutATickAndKeepsItForThePlaySession()
    {
        using OverlayRig rig = new(CreateHost());
        Run run = rig.Host.Run;

        rig.Open();
        rig.Press(Key.T);

        Assert.Equal("Time Scale", rig.Overlay.Title);
        AssertLadder(rig, run.TimeScale);

        rig.Press(Key.Down);
        rig.Press(Key.Enter);

        // The pick lands on the run, where the game reads it, and on the applied value.
        Assert.Equal(0.5, run.TimeScale);
        Assert.Equal(0.5, rig.Scheduler.TimeScale);
        AssertLadder(rig, 0.5);
        Assert.Equal(1, rig.Overlay.Focus);
        Assert.Equal(0, rig.Scheduler.Tick);

        // Closed, a frame worth one step buys half of one.
        rig.Press(Key.Backspace);
        rig.Frame(DeviceSnapshot.Of(Key.Grave));

        Assert.False(rig.Overlay.IsOpen);
        Assert.Equal(0, rig.Scheduler.Tick);

        rig.Frame();

        Assert.Equal(1, rig.Scheduler.Tick);

        // Reopened, hidden, shown again and across a Restart, the overlay never resets it.
        rig.Press(Key.Grave);
        rig.Press(Key.H);
        rig.Press(Key.H);
        rig.Press(Key.R);
        Assert.IsType<ReadoutScene>(rig.Host.Scene);

        rig.Press(Key.T);

        Assert.Equal(0.5, run.TimeScale);
        AssertLadder(rig, 0.5);
    }

    [Fact]
    public void TheTimeScalePage_MarksThePaceTheGameSetAndNoRowForOneOffTheLadder()
    {
        using OverlayRig rig = new(CreateHost());
        Run run = rig.Host.Run;
        run.TimeScale = 2;

        rig.Open();
        rig.Press(Key.T);

        AssertLadder(rig, 2);

        run.TimeScale = 1.5;
        rig.Press(Key.Backspace);
        rig.Press(Key.T);

        AssertLadder(rig, 1.5);

        rig.Press(Key.Enter);

        Assert.Equal(0.25, run.TimeScale);
        AssertLadder(rig, 0.25);
    }

    [Theory]
    [InlineData(1079, 1)]
    [InlineData(1080, 2)]
    [InlineData(2159, 2)]
    [InlineData(2160, 3)]
    public void TheOverlaysScale_StepsWithTheBackBuffersHeight(int height, int scale) =>
        Assert.Equal(scale, OverlayHost.ScaleFor(height));

    private static SceneRegistration Unclaimed() =>
        SceneRegistration.DocumentOnly(UnclaimedDocument, static content => new Scene(content!.Value));

    // A mark on exactly the row whose pace is in force, none where the pace is off the ladder.
    private static void AssertLadder(OverlayRig rig, double pace)
    {
        string[] labels = rig.Rows();

        Assert.Equal(OverlayHost.TimeScales.Length, labels.Length);
        for (int index = 0; index < labels.Length; index++)
        {
            (double scale, string label) = OverlayHost.TimeScales[index];

            Assert.EndsWith(label, labels[index], StringComparison.Ordinal);
            Assert.Equal(scale == pace, labels[index].StartsWith("(x)", StringComparison.Ordinal));
        }
    }
}
