using System.Numerics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

// The debug draw buffer is one process-wide slot, attached by whichever overlay was built last.
[Collection(LogSinkCollection.Name)]
public sealed class DebugInspectionTests
{
    private const double StepSeconds = 0.1;

    // The list is the scene's order, the second of a type suffixed by its place among that type.
    [Fact]
    public void I_ListsTheEntitiesInSceneOrderWithDuplicateSuffixesAndEnterOpensAPanel()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using DebugOverlay overlay = new(Key.Grave, scheduler, host, host);
        DebugScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.I);

        Assert.Equal("Inspect", scene.Title);
        Assert.Equal(["Lone", "Walker", "Vanisher", "Walker (1)"], Labels(scene));
        Assert.Equal(2, scene.Depth);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Lone", scene.Title);
        Assert.Equal(3, scene.Depth);
        Assert.Equal("Position  (5, 6)", scene.RowText(0));
        Assert.Equal("ZIndex    3", scene.RowText(1));
        Assert.Equal("Name      solitary", scene.RowText(2));
        Assert.Equal("[Tag]", scene.RowText(3));
        Assert.Equal("Label     one", scene.RowText(4));
        Assert.Equal(5, scene.RowCount);

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Walker (1)", scene.Title);
        Assert.Equal(3, scene.Depth);
        Assert.Equal("Position  (20, 0)", scene.RowText(0));
    }

    [Fact]
    public void AStep_RefreshesThePanelInPlaceAndTheListBeneathIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using DebugOverlay overlay = new(Key.Grave, scheduler, host, host);
        DebugScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.I);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Position  (20, 0)", scene.RowText(0));

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Walker (1)", scene.Title);
        Assert.Equal(3, scene.Depth);
        Assert.Equal("Position  (21, 0)", scene.RowText(0));
        Assert.Equal(1, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal(["Lone", "Walker", "Vanisher", "Walker (1)"], Labels(scene));
        Assert.Equal(3, scene.FocusedIndex);
    }

    [Fact]
    public void AnEntityRemovedByAStep_PopsItsPanelWithTheStatusLineAndLeavesTheList()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using DebugOverlay overlay = new(Key.Grave, scheduler, host, host);
        DebugScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.I);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Vanisher", scene.Title);

        Press(overlay, scheduler, host, Key.Right);
        Assert.Equal("Vanisher", scene.Title);
        Assert.Equal(1, scheduler.Tick);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(2, scheduler.Tick);
        Assert.Equal("Inspect", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Equal("Vanisher left the scene", scene.Status);
        Assert.Equal(["Lone", "Walker", "Walker (1)"], Labels(scene));
        Assert.Equal(2, scene.FocusedIndex);
    }

    // The suffix is a place in scene order, so a panel's title drops one when an earlier entity
    // of its type leaves, matching the list beneath.
    [Fact]
    public void APanelsSuffix_FollowsTheListWhenAnEarlierEntityOfItsTypeLeaves()
    {
        using SceneHost host = CreateHost(new Departing());
        FixedStepScheduler scheduler = CreateScheduler();
        using DebugOverlay overlay = new(Key.Grave, scheduler, host, host);
        DebugScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.I);
        Assert.Equal(["Vanisher", "Lone", "Vanisher (1)"], Labels(scene));

        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Vanisher (1)", scene.Title);

        Press(overlay, scheduler, host, Key.Right);
        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(2, scheduler.Tick);
        Assert.Equal("Vanisher", scene.Title);
        Assert.Equal(3, scene.Depth);

        Press(overlay, scheduler, host, Key.Backspace);
        Assert.Equal(["Lone", "Vanisher"], Labels(scene));
    }

    [Fact]
    public void ALoad_RefillsTheListFromTheNewSceneAndAnEmptySceneSaysSo()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using DebugOverlay overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        DebugScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.I);
        Assert.Equal(["Lone", "Walker", "Vanisher", "Walker (1)"], Labels(scene));

        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.IsType<OtherScene>(host.Scene);
        Assert.Equal("Load Scene", scene.Title);

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal("Inspect", scene.Title);
        Assert.Equal(["Lone"], Labels(scene));

        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.IsType<EmptyScene>(host.Scene);

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal(1, scene.Depth);
        Assert.Equal("No entities in the scene", scene.Status);

        Press(overlay, scheduler, host, Key.I);

        Assert.Equal(1, scene.Depth);
        Assert.Equal("No entities in the scene", scene.Status);
    }

    // A panel open across a load: its entity's scene is gone, so it pops to the list, which is
    // refilled from the new scene.
    [Fact]
    public void ALoadWithAPanelOpen_PopsToTheRefilledList()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using DebugOverlay overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        DebugScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.I);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Lone", scene.Title);

        overlay.Load(SceneTransition.ToScene(typeof(OtherScene), null));

        Assert.Equal("Inspect", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Equal("Lone left the scene", scene.Status);
        Assert.Equal(["Lone"], Labels(scene));
    }

    // The root hotkey fires at any depth: with the list already stacked it neither stacks a
    // second nor replaces the one that later steps refresh.
    [Fact]
    public void I_WithAnInspectMenuAlreadyStacked_DoesNothing()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using DebugOverlay overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        DebugScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.I);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Walker", scene.Title);
        Assert.Equal(3, scene.Depth);

        Press(overlay, scheduler, host, Key.I);

        Assert.Equal("Walker", scene.Title);
        Assert.Equal(3, scene.Depth);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal("Position  (11, 0)", scene.RowText(0));

        Press(overlay, scheduler, host, Key.L);
        Assert.Equal("Load Scene", scene.Title);
        Assert.Equal(4, scene.Depth);

        Press(overlay, scheduler, host, Key.I);

        Assert.Equal("Load Scene", scene.Title);
        Assert.Equal(4, scene.Depth);

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal("Inspect", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Equal(["Lone", "Walker", "Vanisher", "Walker (1)"], Labels(scene));
    }

    // Closed, the game runs; reopened, the panel shows where it got to, and a scene it replaced
    // pops the panel as a step would.
    [Fact]
    public void ReopeningAfterTheGameRan_RefreshesThePanelOrPopsItWithTheDepartedScene()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using DebugOverlay overlay = new(Key.Grave, scheduler, host, host);
        DebugScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.I);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Position  (10, 0)", scene.RowText(0));

        Press(overlay, scheduler, host, Key.Grave);
        Assert.False(overlay.IsOpen);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        long ran = scheduler.Tick;
        Assert.True(ran >= 2);

        Press(overlay, scheduler, host, Key.Grave);

        Assert.True(overlay.IsOpen);
        Assert.Equal("Walker", scene.Title);
        Assert.Equal($"Position  ({10 + ran}, 0)", scene.RowText(0));

        Press(overlay, scheduler, host, Key.Grave);
        host.Run.RequestScene<OtherScene>();
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Assert.IsType<OtherScene>(host.Scene);

        Press(overlay, scheduler, host, Key.Grave);

        Assert.True(overlay.IsOpen);
        Assert.Equal("Inspect", scene.Title);
        Assert.Equal("Walker left the scene", scene.Status);
        Assert.Equal(["Lone"], Labels(scene));
    }

    // Sections are set apart by a blank row the focus never lands on, and a component that
    // reports nothing says so.
    [Fact]
    public void APanel_SeparatesComponentSectionsWithABlankRowAndNotesAnEmptyOne()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using DebugOverlay overlay = new(Key.Grave, scheduler, host, host);
        DebugScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.I);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Vanisher", scene.Title);
        Assert.Equal(
            ["Position  (0, 0)", "ZIndex    0", "[Tag]", "Label     v", "", "[Mute]", "<Nothing to inspect>"],
            Labels(scene));

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Assert.Equal(3, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Down);
        Assert.Equal(5, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Down);
        Assert.Equal(0, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Up);
        Assert.Equal(5, scene.FocusedIndex);
    }

    private static string[] Labels(DebugScene scene)
    {
        IReadOnlyList<DebugMenuItem> items = scene.Menu.Items;
        string[] labels = new string[items.Count];
        for (int index = 0; index < items.Count; index++)
        {
            labels[index] = items[index].Label;
        }

        return labels;
    }

    private static void Open(DebugOverlay overlay, FixedStepScheduler scheduler, ISimulation simulation)
    {
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(Key.Grave));
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Empty);

        Assert.True(overlay.IsOpen);
    }

    private static void Press(DebugOverlay overlay, FixedStepScheduler scheduler, ISimulation simulation, Key key)
    {
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Of(key));
        Frame(overlay, scheduler, simulation, DeviceSnapshot.Empty);
    }

    private static void Frame(DebugOverlay overlay, FixedStepScheduler scheduler, ISimulation simulation, DeviceSnapshot sampled)
    {
        DeviceSnapshot stripped = overlay.Observe(sampled);
        scheduler.Advance(StepSeconds, stripped, simulation);
        overlay.Step();
    }

    private static FixedStepScheduler CreateScheduler() => new(StepSeconds, 5, new ActionBindings());

    private static SceneHost CreateHost(Scene? first = null) =>
        new(
            SceneTransition.ToScene(first?.GetType() ?? typeof(Populated), null),
            (in SceneTransition target) => target.SceneType switch
            {
                Type type when first is not null && type == first.GetType() => first,
                Type type when type == typeof(Populated) => new Populated(),
                Type type when type == typeof(OtherScene) => new OtherScene(),
                Type type when type == typeof(EmptyScene) => new EmptyScene(),
                _ => throw new InvalidOperationException($"Unexpected transition {target.Kind}."),
            },
            new Run());

    private static SceneRegistry CreateRegistry() =>
        new(
            new EntityRegistry([]),
            [
                SceneRegistration.Plain(typeof(EmptyScene), static () => new EmptyScene()),
                SceneRegistration.Plain(typeof(OtherScene), static () => new OtherScene()),
            ]);

    // One Lone, one Vanisher that leaves on the second tick, two Walkers a unit apart in scene order.
    private sealed class Populated : Scene
    {
        internal Populated()
        {
            Add(new Lone(new Vector2(5f, 6f)) { ZIndex = 3 });
            Add(new Walker(new Vector2(10f, 0f)));
            Add(new Vanisher(Vector2.Zero));
            Add(new Walker(new Vector2(20f, 0f)));
        }
    }

    // Two Vanishers around a Lone; only the first leaves.
    private sealed class Departing : Scene
    {
        internal Departing()
        {
            Add(new Vanisher(Vector2.Zero));
            Add(new Lone(Vector2.Zero));
            Add(new Vanisher(Vector2.Zero, leavesOn: long.MaxValue));
        }
    }

    private sealed class OtherScene : Scene
    {
        internal OtherScene() => Add(new Lone(Vector2.Zero));
    }

    private sealed class EmptyScene : Scene;

    private sealed class Lone : Entity
    {
        internal Lone(Vector2 position)
            : base(position) => Add(new Tag("one"));

        protected internal override void OnInspect(Capsule.Diagnostics.Inspector inspector) =>
            inspector.Field("Name", "solitary");
    }

    private sealed class Tag(string label) : Component
    {
        protected internal override void OnInspect(Capsule.Diagnostics.Inspector inspector) =>
            inspector.Field("Label", label);
    }

    private sealed class Walker(Vector2 position) : Entity(position)
    {
        protected internal override void OnStep(in StepContext context) => Position += Vector2.UnitX;
    }

    private sealed class Mute : Component;

    private sealed class Vanisher : Entity
    {
        private readonly long _leavesOn;

        internal Vanisher(Vector2 position, long leavesOn = 1)
            : base(position)
        {
            _leavesOn = leavesOn;
            Add(new Tag("v"));
            Add(new Mute());
        }

        protected internal override void OnStep(in StepContext context)
        {
            if (context.Tick >= _leavesOn)
            {
                Scene!.Remove(this);
            }
        }
    }
}
