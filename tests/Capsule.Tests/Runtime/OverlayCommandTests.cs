using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.OverlayRig;
using static Capsule.Tests.Runtime.PanelFixtures;

namespace Capsule.Tests.Runtime;

// What a panel's commands and toggles do to the run, and what a step or a load does to the pages over
// it.
// A failed command logs, and the sink is one process-wide slot.
[Collection(LogSinkCollection.Name)]
public sealed class OverlayCommandTests
{
    // A command or toggle runs between ticks and is followed by exactly one stepped tick, after which
    // the pages are rebuilt: the toggle shows its new state, the fields re-read, the focus stays. A
    // section's commands sit under their own sub-heading after its fields whatever order the hook
    // wrote them in.
    [Fact]
    public void ACommandOrToggle_RunsThenStepsOnceAndRebuildsThePagesWithTheToggleShown()
    {
        Seamed seamed = new();
        using SceneHost host = CreateHost(seamed);
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);

        Assert.Equal("Seamed", overlay.Title);
        Assert.Equal(
            [.. Head[..11], "Spawned", "  (Commands)", "  Spawn", "  [ ] Slow", "", "[Entities]", "Nudger"],
            Named(overlay));
        Assert.Equal(13, overlay.Focus);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(1, seamed.Spawned);
        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Spawned          1", Drawn(overlay, 11));
        Assert.Equal(13, overlay.Focus);

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.True(seamed.Slow);
        Assert.Equal(2, scheduler.Tick);
        Assert.Equal("  [x] Slow", Drawn(overlay, 14));
        Assert.Equal(14, overlay.Focus);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.False(seamed.Slow);
        Assert.Equal("  [ ] Slow", Drawn(overlay, 14));

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Nudger", overlay.Title);
        Assert.Equal(
            ["[Entity]", "Transform", "ZIndex", "ScrollFactor", "Tint", "Flash", "StepMode", "  (Commands)", "  [x] Visible", "  Remove", "  Nudge"],
            Named(overlay));
        Assert.Equal(8, overlay.Focus);

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Transform     (2, 2) r 0 s (1, 1)", Drawn(overlay, 1));
        Assert.Equal(3, overlay.Depth);
        Assert.Equal(4, scheduler.Tick);

        // The page beneath is rebuilt from the scene as it now stands on the way back to it.
        seamed.Spawned = 5;
        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal("Seamed", overlay.Title);
        Assert.Equal("Spawned          5", Drawn(overlay, 11));
        Assert.Equal(17, overlay.Focus);
    }

    // A transition a command asks the run for is consumed by the command's own tick, as a load row's
    // is; a start that fails shows on the status line and the run stays on its scene.
    [Fact]
    public void ACommandThatRequestsAScene_IsConsumedByItsTickAndAFailedStartShowsOnTheStatusLine()
    {
        Log.UseSink(null);
        Requesting requesting = new();
        using SceneHost host = CreateHost(requesting);
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal([.. Head[..11], "  (Commands)", "  Break", "  Next", "", "[Entities]", "Lone"], Named(overlay));
        Assert.Equal(12, overlay.Focus);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.StartsWith("Command failed", overlay.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), overlay.Status, StringComparison.Ordinal);
        Assert.Same(requesting, host.Scene);
        Assert.True(scheduler.Held);
        Assert.Equal("Requesting", overlay.Title);

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.IsType<OtherScene>(host.Scene);
        Assert.Equal("OtherScene", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        Assert.Equal([.. Head, "Lone"], Named(overlay));
        Assert.Equal(string.Empty, overlay.Status);
    }

    [Fact]
    public void AStep_RefreshesThePanelInPlaceAndThePageBeneathIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Transform     (20, 0) r 0 s (1, 1)", Drawn(overlay, 1));

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Walker (1)", overlay.Title);
        Assert.Equal(3, overlay.Depth);
        Assert.Equal("Transform     (21, 0) r 0 s (1, 1)", Drawn(overlay, 1));

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], Named(overlay));
        Assert.Equal(FirstEntity + 3, overlay.Focus);
    }

    [Fact]
    public void AnEntityRemovedByAStep_PopsItsPanelWithTheStatusLineAndLeavesThePage()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Vanisher", overlay.Title);

        Press(overlay, scheduler, host, Key.Right);
        Assert.Equal("Vanisher", overlay.Title);
        Assert.Equal(1, scheduler.Tick);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(2, scheduler.Tick);
        Assert.Equal("Populated", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        Assert.Contains("Vanisher", overlay.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone", "Walker", "Walker (1)"], Named(overlay));
        Assert.Equal(FirstEntity + 2, overlay.Focus);
    }

    // The engine's own Remove command on an entity: the tick after it takes the entity out, and the
    // panel pops as for any removed subject.
    [Fact]
    public void TheRemoveCommand_TakesTheEntityOutAndPopsItsPanel()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Walker", overlay.Title);
        Press(overlay, scheduler, host, Key.Down);
        Assert.Equal("  Remove", Drawn(overlay, overlay.Focus));

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Populated", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        Assert.Contains("Walker", overlay.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone", "Vanisher", "Walker"], Named(overlay));
    }

    // Only a transition that fails to bring its scene up is a status-line matter; the tick's own
    // failure after a command is the game's crash, as it is from the Step row.
    [Fact]
    public void ACommandWhoseFollowingStepThrows_Propagates()
    {
        using SceneHost host = CreateHost(new Brittle());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal([.. Head[..11], "  (Commands)", "  Arm", "", "[Entities]", "<Nothing to show>"], Named(overlay));

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter)));

        Assert.Equal("armed", thrown.Message);
        Assert.Equal(string.Empty, overlay.Status);
    }

    [Fact]
    public void ALoad_RefillsThePageFromTheNewSceneAndAnEmptySceneShowsThePlaceholder()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], Named(overlay));

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.IsType<OtherScene>(host.Scene);
        Assert.Equal("Load Scene", overlay.Title);

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.S);

        Assert.Equal("OtherScene", overlay.Title);
        Assert.Equal([.. Head, "Lone"], Named(overlay));

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.IsType<EmptyScene>(host.Scene);

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.S);

        Assert.Equal("EmptyScene", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        Assert.Equal([.. Head, "<Nothing to show>"], Named(overlay));
    }

    // A panel open across a load: its entity's scene is gone, so the next frame pops it to the page,
    // which is built from the new scene.
    [Fact]
    public void ALoadWithAPanelOpen_PopsToTheRefilledPage()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Lone", overlay.Title);

        overlay.Load(SceneTransition.ToScene(typeof(OtherScene), null));
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);

        Assert.Equal("OtherScene", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        Assert.Contains("Lone", overlay.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone"], Named(overlay));
    }

    // A step from its parent's panel takes the child out and leaves the child's panel beneath stale;
    // the frame whose Back exposes it shows the page it pops to, never the departed entity's panel.
    [Fact]
    public void ABackThatExposesAStalePanel_PopsItOnTheSameFrame()
    {
        using SceneHost host = CreateHost(new Parented());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal([.. Head, "Walker", "  Vanisher"], Named(overlay));

        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Vanisher", overlay.Title);

        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Walker", overlay.Title);
        Assert.Equal(4, overlay.Depth);

        Press(overlay, scheduler, host, Key.Right);
        Press(overlay, scheduler, host, Key.Right);
        Assert.Equal(2, scheduler.Tick);
        Assert.Equal("Walker", overlay.Title);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Backspace));

        Assert.Equal("Parented", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        Assert.Contains("Vanisher", overlay.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Walker"], Named(overlay));
    }
}
