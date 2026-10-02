using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.OverlayRig;
using static Capsule.Tests.Runtime.PanelFixtures;

namespace Capsule.Tests.Runtime;

// What a panel's commands do to the run, and what a step or a load does to the pages over it. A failed
// command logs to the process-wide sink.
[Collection(LogSinkCollection.Name)]
public sealed class OverlayCommandTests
{
    // A command or toggle runs inside exactly one stepped tick, and the pages are rebuilt after it. A
    // section's commands follow its fields under their own sub-heading.
    [Fact]
    public void ACommandOrToggle_RunsThenStepsOnceAndRebuildsThePagesWithTheToggleShown()
    {
        Seamed seamed = new();
        using OverlayRig rig = new(CreateHost(seamed));

        rig.Open();
        rig.Press(Key.S);

        Assert.Equal("Seamed", rig.Overlay.Title);
        Assert.Equal(
            [.. Head[..11], "Spawned", "  (Commands)", "  Spawn", "  [ ] Slow", "", "[Entities]", "Nudger"],
            rig.Rows());
        Assert.Equal(13, rig.Overlay.Focus);

        rig.Press(Key.Enter);

        Assert.Equal(1, seamed.Spawned);
        Assert.Equal(1, rig.Scheduler.Tick);
        Assert.Equal("Spawned          1", Drawn(rig, 11));
        Assert.Equal(13, rig.Overlay.Focus);

        rig.Press(Key.Down);
        rig.Press(Key.Enter);

        Assert.True(seamed.Slow);
        Assert.Equal(2, rig.Scheduler.Tick);
        Assert.Equal("  [x] Slow", Drawn(rig, 14));
        Assert.Equal(14, rig.Overlay.Focus);

        rig.Press(Key.Enter);

        Assert.False(seamed.Slow);
        Assert.Equal("  [ ] Slow", Drawn(rig, 14));

        rig.Press(Key.Down);
        rig.Press(Key.Enter);

        Assert.Equal("Nudger", rig.Overlay.Title);
        Assert.Equal(
            ["[Entity]", "Transform", "ZIndex", "ScrollFactor", "Tint", "Flash", "StepMode", "  (Commands)", "  [x] Visible", "  Remove", "  Nudge"],
            rig.Rows());
        Assert.Equal(8, rig.Overlay.Focus);

        rig.Press(Key.Down);
        rig.Press(Key.Down);
        rig.Press(Key.Enter);

        Assert.Equal("Transform     (2, 2) r 0 s (1, 1)", Drawn(rig, 1));
        Assert.Equal(3, rig.Overlay.Depth);
        Assert.Equal(4, rig.Scheduler.Tick);

        // The page beneath is rebuilt from the scene as it now stands on the way back to it.
        seamed.Spawned = 5;
        rig.Press(Key.Backspace);

        Assert.Equal("Seamed", rig.Overlay.Title);
        Assert.Equal("Spawned          5", Drawn(rig, 11));
        Assert.Equal(17, rig.Overlay.Focus);
    }

    // A Back and a reopen each rebuild the page once, and a frame without an act rebuilds nothing.
    [Fact]
    public void AnAct_RunsThePagesHooksOnce()
    {
        Seamed seamed = new();
        using OverlayRig rig = new(CreateHost(seamed));

        rig.Open();
        rig.Press(Key.S);
        rig.Press(Key.Up);
        rig.Press(Key.Enter);
        Assert.Equal("Nudger", rig.Overlay.Title);
        int built = seamed.Panels;

        rig.Press(Key.Backspace);

        Assert.Equal(built + 1, seamed.Panels);

        rig.Press(Key.Grave);
        rig.Press(Key.Grave);

        Assert.Equal(built + 2, seamed.Panels);

        rig.Press(Key.Enter);
        Assert.Equal("Nudger", rig.Overlay.Title);
        rig.Press(Key.Down);
        built = seamed.Panels;

        // The removal pops the panel. The press's idle frame does not build the scene page again.
        rig.Press(Key.Enter);

        Assert.Equal(nameof(Seamed), rig.Overlay.Title);
        Assert.Equal(built + 1, seamed.Panels);
    }

    // The command's own tick consumes the transition it requests. A start that fails shows on the
    // status line and the run stays on its scene.
    [Fact]
    public void ACommandThatRequestsAScene_IsConsumedByItsTickAndAFailedStartShowsOnTheStatusLine()
    {
        Log.UseSink(null);
        Requesting requesting = new();
        using OverlayRig rig = new(CreateHost(requesting));

        rig.Open();
        rig.Press(Key.S);
        Assert.Equal([.. Head[..11], "  (Commands)", "  Break", "  Next", "", "[Entities]", "Lone"], rig.Rows());
        Assert.Equal(12, rig.Overlay.Focus);

        rig.Press(Key.Enter);

        Assert.StartsWith("Command failed", rig.Overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), rig.Overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Same(requesting, rig.Host.Scene);
        Assert.True(rig.Scheduler.Held);
        Assert.Equal("Requesting", rig.Overlay.Title);

        rig.Press(Key.Down);
        rig.Press(Key.Enter);

        Assert.IsType<OtherScene>(rig.Host.Scene);
        Assert.Equal("OtherScene", rig.Overlay.Title);
        Assert.Equal(2, rig.Overlay.Depth);
        Assert.Equal([.. Head, "Lone"], rig.Rows());
        Assert.Equal(string.Empty, rig.Overlay.Scene.Status);
    }

    [Fact]
    public void AStep_RefreshesThePanelInPlaceAndThePageBeneathIt()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Press(Key.S);
        rig.Press(Key.Up);
        rig.Press(Key.Enter);
        Assert.Equal("Transform     (20, 0) r 0 s (1, 1)", Drawn(rig, 1));

        rig.Press(Key.Right);

        Assert.Equal(1, rig.Scheduler.Tick);
        Assert.Equal("Walker (1)", rig.Overlay.Title);
        Assert.Equal(3, rig.Overlay.Depth);
        Assert.Equal("Transform     (21, 0) r 0 s (1, 1)", Drawn(rig, 1));

        rig.Press(Key.Backspace);

        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], rig.Rows());
        Assert.Equal(FirstEntity + 3, rig.Overlay.Focus);
    }

    [Fact]
    public void AnEntityRemovedByAStep_PopsItsPanelWithTheStatusLineAndLeavesThePage()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Press(Key.S);
        rig.Press(Key.Down);
        rig.Press(Key.Down);
        rig.Press(Key.Enter);
        Assert.Equal("Vanisher", rig.Overlay.Title);

        rig.Press(Key.Right);
        Assert.Equal("Vanisher", rig.Overlay.Title);
        Assert.Equal(1, rig.Scheduler.Tick);

        rig.Press(Key.Right);

        Assert.Equal(2, rig.Scheduler.Tick);
        Assert.Equal("Populated", rig.Overlay.Title);
        Assert.Equal(2, rig.Overlay.Depth);
        Assert.Contains("Vanisher", rig.Overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone", "Walker", "Walker (1)"], rig.Rows());
        Assert.Equal(FirstEntity + 2, rig.Overlay.Focus);
    }

    [Fact]
    public void TheRemoveCommand_TakesTheEntityOutAndPopsItsPanel()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Press(Key.S);
        rig.Press(Key.Down);
        rig.Press(Key.Enter);
        Assert.Equal("Walker", rig.Overlay.Title);
        rig.Press(Key.Down);
        Assert.Equal("  Remove", Drawn(rig, rig.Overlay.Focus));

        rig.Press(Key.Enter);

        Assert.Equal(1, rig.Scheduler.Tick);
        Assert.Equal("Populated", rig.Overlay.Title);
        Assert.Equal(2, rig.Overlay.Depth);
        Assert.Contains("Walker", rig.Overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone", "Vanisher", "Walker"], rig.Rows());
    }

    // Only a failed scene start is a status-line matter. Any other failure of the tick is the game's
    // crash.
    [Fact]
    public void ACommandWhoseFollowingStepThrows_Propagates()
    {
        using OverlayRig rig = new(CreateHost(new Brittle()));

        rig.Open();
        rig.Press(Key.S);
        Assert.Equal([.. Head[..11], "  (Commands)", "  Arm", "", "[Entities]", "<Nothing to show>"], rig.Rows());

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => rig.Frame(DeviceSnapshot.Of(Key.Enter)));

        Assert.Equal("armed", thrown.Message);
        Assert.Equal(string.Empty, rig.Overlay.Scene.Status);
    }

    [Fact]
    public void ALoad_RefillsThePageFromTheNewSceneAndAnEmptySceneShowsThePlaceholder()
    {
        using OverlayRig rig = new(CreateHost(), CreateRegistry());

        rig.Open();
        rig.Press(Key.S);
        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], rig.Rows());

        rig.Press(Key.Backspace);
        rig.Press(Key.L);
        rig.Press(Key.Down);
        rig.Press(Key.Enter);

        Assert.IsType<OtherScene>(rig.Host.Scene);
        Assert.Equal("Load Scene", rig.Overlay.Title);

        rig.Press(Key.Backspace);
        rig.Press(Key.S);

        Assert.Equal("OtherScene", rig.Overlay.Title);
        Assert.Equal([.. Head, "Lone"], rig.Rows());

        rig.Press(Key.Backspace);
        rig.Press(Key.L);
        rig.Press(Key.Enter);
        Assert.IsType<EmptyScene>(rig.Host.Scene);

        rig.Press(Key.Backspace);
        rig.Press(Key.S);

        Assert.Equal("EmptyScene", rig.Overlay.Title);
        Assert.Equal(2, rig.Overlay.Depth);
        Assert.Equal([.. Head, "<Nothing to show>"], rig.Rows());
    }

    [Fact]
    public void ALoadWithAPanelOpen_PopsToTheRefilledPage()
    {
        using OverlayRig rig = new(CreateHost(), CreateRegistry());

        rig.Open();
        rig.Press(Key.S);
        rig.Press(Key.Enter);
        Assert.Equal("Lone", rig.Overlay.Title);

        rig.Overlay.Load(SceneTransition.ToScene(typeof(OtherScene), null));
        rig.Frame();

        Assert.Equal("OtherScene", rig.Overlay.Title);
        Assert.Equal(2, rig.Overlay.Depth);
        Assert.Contains("Lone", rig.Overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone"], rig.Rows());
    }

    // A step from the parent's panel takes the child out. The Back that exposes the child's panel
    // shows the page it pops to on the same frame.
    [Fact]
    public void ABackThatExposesAStalePanel_PopsItOnTheSameFrame()
    {
        using OverlayRig rig = new(CreateHost(new Parented()));

        rig.Open();
        rig.Press(Key.S);
        Assert.Equal([.. Head, "Walker", "  Vanisher"], rig.Rows());

        rig.Press(Key.Up);
        rig.Press(Key.Enter);
        Assert.Equal("Vanisher", rig.Overlay.Title);

        rig.Press(Key.Enter);
        Assert.Equal("Walker", rig.Overlay.Title);
        Assert.Equal(4, rig.Overlay.Depth);

        rig.Press(Key.Right);
        rig.Press(Key.Right);
        Assert.Equal(2, rig.Scheduler.Tick);
        Assert.Equal("Walker", rig.Overlay.Title);

        rig.Frame(DeviceSnapshot.Of(Key.Backspace));

        Assert.Equal("Parented", rig.Overlay.Title);
        Assert.Equal(2, rig.Overlay.Depth);
        Assert.Contains("Vanisher", rig.Overlay.Scene.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Walker"], rig.Rows());
    }
}
