using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using static Capsule.Tests.Runtime.OverlayRig;
using static Capsule.Tests.Runtime.PanelFixtures;

namespace Capsule.Tests.Runtime;

// The scene page and the entity panels: what they list, how they are named, and how one opens another.
public sealed class OverlayPanelTests
{
    // A scene with no override shows the innate rows and its entities; the list is the scene's order,
    // the second of a type suffixed by its place among that type; a panel's fields are read, not
    // focused.
    [Fact]
    public void TheSceneKey_OpensThePageWithTheSeedAndTheEntitiesInOrderAndEnterOpensAPanel()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);

        Assert.Equal("Populated", overlay.Title);
        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], Named(overlay));
        Assert.Equal(FirstEntity, overlay.Focus);
        Assert.Equal(2, overlay.Depth);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Lone", overlay.Title);
        Assert.Equal(3, overlay.Depth);
        Assert.Equal(
            ["[Entity]", "Transform", "ZIndex", "ScrollFactor", "Tint", "StepMode", "Name", "  (Commands)", "  [x] Visible", "  Remove", "", "[Tag]", "Label"],
            Named(overlay));

        Press(overlay, scheduler, host, Key.Down);
        Assert.Equal(9, overlay.Focus);

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Walker (1)", overlay.Title);
        Assert.Equal(3, overlay.Depth);
        Assert.Equal("Transform     (20, 0) r 0 s (1, 1)", Drawn(overlay, 1));
    }

    // Sections are set apart by a blank row, and a component that writes nothing says so.
    [Fact]
    public void APanel_SeparatesComponentSectionsWithABlankRowAndNotesAnEmptyOne()
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
        Assert.Equal(
            ["[Entity]", "Transform", "ZIndex", "ScrollFactor", "Tint", "StepMode", "  (Commands)", "  [x] Visible", "  Remove", "", "[Tag]", "Label", "", "[Mute]", "<Nothing to show>"],
            Named(overlay));
    }

    // The page lists in tree order, indented by depth, each entity named by its Name or type and
    // suffixed among its siblings alone. A child's panel heads the Entity section with a Parent row
    // naming the parent as the page does, and choosing it opens the parent's panel without a tick.
    [Fact]
    public void AChildsPanel_IsNamedAmongItsSiblingsAndOffersItsParentAsARowThatOpensWithoutATick()
    {
        using SceneHost host = CreateHost(new Nested());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal(
            [.. Head, "Lone", "Walker", "  Spark", "    Entity", "    Entity (1)", "  Entity", "  Spark (1)"],
            Named(overlay));

        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Spark (1)", overlay.Title);
        Assert.Equal(
            ["[Entity]", "Parent", "Name", "Transform", "World Transform", "ZIndex", "ScrollFactor", "Tint", "StepMode", "  (Commands)", "  [x] Visible", "  Remove"],
            Named(overlay));
        Assert.Equal("Parent           Walker", Drawn(overlay, 1));
        Assert.Equal("World Transform  (14, 0) r 0 s (1, 1)", Drawn(overlay, 4));
        Assert.Equal(1, overlay.Focus);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Walker", overlay.Title);
        Assert.Equal(4, overlay.Depth);
        Assert.Equal(0, scheduler.Tick);
        Assert.Equal("Transform     (10, 0) r 0 s (1, 1)", Drawn(overlay, 1));

        Press(overlay, scheduler, host, Key.Right);
        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Transform     (11, 0) r 0 s (1, 1)", Drawn(overlay, 1));

        Press(overlay, scheduler, host, Key.Backspace);
        Assert.Equal("Spark (1)", overlay.Title);
        Assert.Equal("World Transform  (15, 0) r 0 s (1, 1)", Drawn(overlay, 4));

        // The count is the layer's: the second Entity under Spark, not the third in the scene.
        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Entity (1)", overlay.Title);
        Assert.Equal("Parent           Spark", Drawn(overlay, 1));
    }

    // The suffix is a place in scene order, so a panel's title drops one when an earlier entity of its
    // type leaves, matching the page beneath.
    [Fact]
    public void APanelsSuffix_FollowsThePageWhenAnEarlierEntityOfItsTypeLeaves()
    {
        using SceneHost host = CreateHost(new Departing());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal([.. Head, "Vanisher", "Lone", "Vanisher (1)"], Named(overlay));

        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Vanisher (1)", overlay.Title);

        Press(overlay, scheduler, host, Key.Right);
        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(2, scheduler.Tick);
        Assert.Equal("Vanisher", overlay.Title);
        Assert.Equal(3, overlay.Depth);

        Press(overlay, scheduler, host, Key.Backspace);
        Assert.Equal([.. Head, "Lone", "Vanisher"], Named(overlay));
    }

    // Watching an entity page while stepping is the inspector's use: Step fires there, and an opener
    // pressed there stacks nothing over the page, so one Backspace is the page beneath.
    [Fact]
    public void OnAnEntityPage_StepAdvancesTheRunAndTheOpenerKeysDoNothing()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Walker", overlay.Title);
        Assert.Equal(3, overlay.Depth);

        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.L);

        Assert.Equal("Walker", overlay.Title);
        Assert.Equal(3, overlay.Depth);
        Assert.Equal(string.Empty, overlay.Status);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Transform     (11, 0) r 0 s (1, 1)", Drawn(overlay, 1));

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal("Populated", overlay.Title);
        Assert.Equal(2, overlay.Depth);
        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], Named(overlay));
    }

    // Closed, the game runs; reopened, the panel shows where it got to, and a scene it replaced pops
    // the panel as a step would.
    [Fact]
    public void ReopeningAfterTheGameRan_RefreshesThePanelOrPopsItWithTheDepartedScene()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Transform     (10, 0) r 0 s (1, 1)", Drawn(overlay, 1));

        Press(overlay, scheduler, host, Key.Grave);
        Assert.False(overlay.IsOpen);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        long ran = scheduler.Tick;
        Assert.True(ran >= 2);

        Press(overlay, scheduler, host, Key.Grave);

        Assert.True(overlay.IsOpen);
        Assert.Equal("Walker", overlay.Title);
        Assert.Equal($"Transform     ({10 + ran}, 0) r 0 s (1, 1)", Drawn(overlay, 1));

        Press(overlay, scheduler, host, Key.Grave);
        host.Run.RequestScene<OtherScene>();
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Assert.IsType<OtherScene>(host.Scene);

        Press(overlay, scheduler, host, Key.Grave);

        Assert.True(overlay.IsOpen);
        Assert.Equal("OtherScene", overlay.Title);
        Assert.Contains("Walker", overlay.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone"], Named(overlay));
    }
}
