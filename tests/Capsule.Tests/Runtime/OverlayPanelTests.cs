using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using static Capsule.Tests.Runtime.OverlayRig;
using static Capsule.Tests.Runtime.PanelFixtures;

namespace Capsule.Tests.Runtime;

// The scene page and the entity panels: what they list, how they are named, and how one opens another.
public sealed class OverlayPanelTests
{
    // The entities list in scene order, a repeated type suffixed by its place among that type. A
    // panel's fields are read, not focused.
    [Fact]
    public void TheSceneKey_OpensThePageWithTheSeedAndTheEntitiesInOrderAndEnterOpensAPanel()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Press(Key.S);

        Assert.Equal("Populated", rig.Overlay.Title);
        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], rig.Rows());
        Assert.Equal("Camera", rig.Focused());
        Assert.Equal(2, rig.Overlay.Depth);

        rig.Press(Key.Down);
        Assert.Equal(FirstEntity, rig.Overlay.Focus);
        rig.Press(Key.Enter);

        Assert.Equal("Lone", rig.Overlay.Title);
        Assert.Equal(3, rig.Overlay.Depth);
        Assert.Equal(
            ["[Entity]", "Transform", "ZIndex", "ScrollFactor", "Tint", "Flash", "StepMode", "Name", "  (Commands)", "  [x] Visible", "  Remove", "", "[Tag]", "Label"],
            rig.Rows());

        rig.Press(Key.Down);
        Assert.Equal(10, rig.Overlay.Focus);

        rig.Press(Key.Backspace);
        rig.Press(Key.Up);
        rig.Press(Key.Up);
        rig.Press(Key.Enter);

        Assert.Equal("Walker (1)", rig.Overlay.Title);
        Assert.Equal(3, rig.Overlay.Depth);
        Assert.Equal("Transform     (20, 0) r 0 s (1, 1)", Drawn(rig, 1));
    }

    // Sections are set apart by a blank row, and a component that writes nothing says so.
    [Fact]
    public void APanel_SeparatesComponentSectionsWithABlankRowAndNotesAnEmptyOne()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Press(Key.S);
        rig.Press(Key.Down);
        rig.Press(Key.Down);
        rig.Press(Key.Down);
        rig.Press(Key.Enter);

        Assert.Equal("Vanisher", rig.Overlay.Title);
        Assert.Equal(
            ["[Entity]", "Transform", "ZIndex", "ScrollFactor", "Tint", "Flash", "StepMode", "  (Commands)", "  [x] Visible", "  Remove", "", "[Tag]", "Label", "", "[Mute]", "<Nothing to show>"],
            rig.Rows());
    }

    // The page lists in tree order, indented by depth, each entity suffixed among its siblings alone.
    // A child's Parent row opens the parent's panel without a tick.
    [Fact]
    public void AChildsPanel_IsNamedAmongItsSiblingsAndOffersItsParentAsARowThatOpensWithoutATick()
    {
        using OverlayRig rig = new(CreateHost(new Nested()));

        rig.Open();
        rig.Press(Key.S);
        Assert.Equal(
            [.. Head, "Lone", "Walker", "  Spark", "    Entity", "    Entity (1)", "  Entity", "  Spark (1)"],
            rig.Rows());

        rig.Press(Key.Up);
        rig.Press(Key.Enter);

        Assert.Equal("Spark (1)", rig.Overlay.Title);
        Assert.Equal(
            ["[Entity]", "Parent", "Name", "Transform", "World Transform", "ZIndex", "ScrollFactor", "Tint", "Flash", "StepMode", "  (Commands)", "  [x] Visible", "  Remove"],
            rig.Rows());
        Assert.Equal("Parent           Walker", Drawn(rig, 1));
        Assert.Equal("World Transform  (14, 0) r 0 s (1, 1)", Drawn(rig, 4));
        Assert.Equal(1, rig.Overlay.Focus);

        rig.Press(Key.Enter);

        Assert.Equal("Walker", rig.Overlay.Title);
        Assert.Equal(4, rig.Overlay.Depth);
        Assert.Equal(0, rig.Scheduler.Tick);
        Assert.Equal("Transform     (10, 0) r 0 s (1, 1)", Drawn(rig, 1));

        rig.Press(Key.Right);
        Assert.Equal(1, rig.Scheduler.Tick);
        Assert.Equal("Transform     (11, 0) r 0 s (1, 1)", Drawn(rig, 1));

        rig.Press(Key.Backspace);
        Assert.Equal("Spark (1)", rig.Overlay.Title);
        Assert.Equal("World Transform  (15, 0) r 0 s (1, 1)", Drawn(rig, 4));

        // The count is the layer's: the second Entity under Spark, not the third in the scene.
        rig.Press(Key.Backspace);
        rig.Press(Key.Up);
        rig.Press(Key.Up);
        rig.Press(Key.Enter);
        Assert.Equal("Entity (1)", rig.Overlay.Title);
        Assert.Equal("Parent           Spark", Drawn(rig, 1));
    }

    // A panel's title drops its suffix when an earlier entity of its type leaves.
    [Fact]
    public void APanelsSuffix_FollowsThePageWhenAnEarlierEntityOfItsTypeLeaves()
    {
        using OverlayRig rig = new(CreateHost(new Departing()));

        rig.Open();
        rig.Press(Key.S);
        Assert.Equal([.. Head, "Vanisher", "Lone", "Vanisher (1)"], rig.Rows());

        rig.Press(Key.Up);
        rig.Press(Key.Enter);
        Assert.Equal("Vanisher (1)", rig.Overlay.Title);

        rig.Press(Key.Right);
        rig.Press(Key.Right);

        Assert.Equal(2, rig.Scheduler.Tick);
        Assert.Equal("Vanisher", rig.Overlay.Title);
        Assert.Equal(3, rig.Overlay.Depth);

        rig.Press(Key.Backspace);
        Assert.Equal([.. Head, "Lone", "Vanisher"], rig.Rows());
    }

    [Fact]
    public void ReopeningAfterTheGameRan_RefreshesThePanel()
    {
        using OverlayRig rig = new(CreateHost());

        rig.Open();
        rig.Press(Key.S);
        rig.Press(Key.Down);
        rig.Press(Key.Down);
        rig.Press(Key.Enter);
        Assert.Equal("Transform     (10, 0) r 0 s (1, 1)", Drawn(rig, 1));

        rig.Press(Key.Grave);
        Assert.False(rig.Overlay.IsOpen);
        rig.Frame();
        rig.Frame();
        long ran = rig.Scheduler.Tick;
        Assert.True(ran >= 2);

        rig.Press(Key.Grave);

        Assert.True(rig.Overlay.IsOpen);
        Assert.Equal("Walker", rig.Overlay.Title);
        Assert.Equal($"Transform     ({10 + ran}, 0) r 0 s (1, 1)", Drawn(rig, 1));
    }

    [Fact]
    public void AFieldValueLongerThanAnyBuffer_ShowsWhole()
    {
        using OverlayRig rig = new(CreateHost(new LongField()));

        rig.Open();
        rig.Press(Key.S);

        Assert.Contains(rig.Overlay.Scene.ShownRows(), static row => row.EndsWith(LongField.Value, StringComparison.Ordinal));
    }

    // The scene page's Camera row opens the camera's panel without a tick: the engine's rows, then the
    // subclass's own.
    [Fact]
    public void TheCameraRow_OpensTheCamerasPanelWithTheEngineRowsThenTheSubclasses()
    {
        using OverlayRig rig = new(CreateHost(new Tracked()));

        rig.Open();
        rig.Press(Key.S);
        Assert.Equal("Camera      TrackingCamera", Drawn(rig, 1));

        rig.Press(Key.Enter);

        Assert.Equal("TrackingCamera", rig.Overlay.Title);
        Assert.Equal(
            ["[Camera]", "Center", "Zoom", "ViewportSize", "Fit", "Offset", "Bounds", "VisibleRegion", "Lead"],
            rig.Rows());
        Assert.EndsWith("(0, 0) to (64, 32)", Drawn(rig, 6), StringComparison.Ordinal);
        Assert.Equal(0, rig.Scheduler.Tick);
    }

    internal sealed class Tracked : Scene
    {
        internal Tracked() =>
            Camera = new TrackingCamera { ViewportSize = new Vector2(32f, 16f), Bounds = new Rect(0f, 0f, 64f, 32f) };
    }

    internal sealed class TrackingCamera : Camera
    {
        protected internal override void OnDebugPanel(DebugPanel panel) => panel.Field("Lead", 3f);
    }

    internal sealed class LongField : Scene
    {
        internal static readonly string Value = new string('x', 300) + "end";

        protected override void OnDebugPanel(DebugPanel panel) => panel.Field("Long", Value);
    }
}
