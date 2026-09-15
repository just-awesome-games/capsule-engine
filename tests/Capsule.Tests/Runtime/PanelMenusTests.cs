using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Runtime;

// The debug draw buffer is one process-wide slot, attached by whichever overlay was built last.
[Collection(LogSinkCollection.Name)]
public sealed class PanelMenusTests
{
    private const double StepSeconds = 0.1;
    private const ulong Seed = 42;

    // The page's head for a scene with no hook of its own: the Scene section holding the engine's
    // own rows, then the Entities heading. The first entity row is at index FirstEntity. Rows are
    // named rather than spelt out — what each one reads is DebugPanelTests' to hold.
    private static readonly string[] Head =
    [
        "[Scene]",
        "Seed",
        "Size",
        "ClearColor",
        "Sampling",
        "Camera",
        string.Empty,
        "[Entities]",
    ];

    private const int FirstEntity = 8;

    // A scene with no override shows the innate row and its entities; the list is the scene's
    // order, the second of a type suffixed by its place among that type; a panel's rows are read,
    // not focused.
    [Fact]
    public void TheSceneKey_OpensThePageWithTheSeedAndTheEntitiesInOrderAndEnterOpensAPanel()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);

        Assert.Equal("Populated", scene.Title);
        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], Rows(scene));
        Assert.Equal(FirstEntity, scene.FocusedIndex);
        Assert.Equal(2, scene.Depth);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Lone", scene.Title);
        Assert.Equal(3, scene.Depth);
        Assert.Equal(
            ["[Entity]", "Position", "ZIndex", "Name", "  (Commands)", "  Remove", "", "[Tag]", "Label"],
            Rows(scene));

        Press(overlay, scheduler, host, Key.Down);
        Assert.Equal(5, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Walker (1)", scene.Title);
        Assert.Equal(3, scene.Depth);
        Assert.Equal("Position  (20, 0)", scene.RowText(1));
    }

    // A command or toggle runs between ticks and is followed by exactly one stepped tick, after
    // which every page is rebuilt as after a step: the toggle shows its new state, the fields
    // re-read, the focus stays. A section's commands sit under their own sub-heading after its
    // fields whatever order the hook wrote them in.
    [Fact]
    public void ACommandOrToggle_RunsThenStepsOnceAndRebuildsThePagesWithTheToggleShown()
    {
        Seamed seamed = new();
        using SceneHost host = CreateHost(seamed);
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);

        Assert.Equal("Seamed", scene.Title);
        Assert.Equal(
            [.. Head[..6], "Spawned", "  (Commands)", "  Spawn", "  [ ] Slow", "", "[Entities]", "Nudger"],
            Rows(scene));
        Assert.Equal(8, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(1, seamed.Spawned);
        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Spawned     1", scene.RowText(6));
        Assert.Equal(8, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.True(seamed.Slow);
        Assert.Equal(2, scheduler.Tick);
        Assert.Equal("  [x] Slow", scene.RowText(9));
        Assert.Equal(9, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.False(seamed.Slow);
        Assert.Equal("  [ ] Slow", scene.RowText(9));

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Nudger", scene.Title);
        Assert.Equal(["[Entity]", "Position", "ZIndex", "  (Commands)", "  Remove", "  Nudge"], Rows(scene));
        Assert.Equal(4, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Position  (2, 2)", scene.RowText(1));
        Assert.Equal(3, scene.Depth);
        Assert.Equal(4, scheduler.Tick);

        // The page beneath was stale from the nudge and is rebuilt on its way back to current.
        seamed.Spawned = 5;
        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal("Seamed", scene.Title);
        Assert.Equal("Spawned     5", scene.RowText(6));
        Assert.Equal(12, scene.FocusedIndex);
    }

    // A transition a command asks the run for is consumed by the command's own tick, as a menu
    // load's is; a start that fails shows on the status line and the run stays on its scene.
    [Fact]
    public void ACommandThatRequestsAScene_IsConsumedByItsTickAndAFailedStartShowsOnTheStatusLine()
    {
        Log.UseSink(null);
        Requesting requesting = new();
        using SceneHost host = CreateHost(requesting);
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal([.. Head[..6], "  (Commands)", "  Break", "  Next", "", "[Entities]", "Lone"], Rows(scene));
        Assert.Equal(7, scene.FocusedIndex);

        Press(overlay, scheduler, host, Key.Enter);

        Assert.StartsWith("Command failed", scene.Status, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), scene.Status, StringComparison.Ordinal);
        Assert.Same(requesting, host.Scene);
        Assert.True(scheduler.Held);
        Assert.Equal("Requesting", scene.Title);

        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.IsType<OtherScene>(host.Scene);
        Assert.Equal("OtherScene", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Equal([.. Head, "Lone"], Rows(scene));
        Assert.Equal(string.Empty, scene.Status);
    }

    [Fact]
    public void AStep_RefreshesThePanelInPlaceAndThePageBeneathIt()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Position  (20, 0)", scene.RowText(1));

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Walker (1)", scene.Title);
        Assert.Equal(3, scene.Depth);
        Assert.Equal("Position  (21, 0)", scene.RowText(1));

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], Rows(scene));
        Assert.Equal(FirstEntity + 3, scene.FocusedIndex);
    }

    [Fact]
    public void AnEntityRemovedByAStep_PopsItsPanelWithTheStatusLineAndLeavesThePage()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Vanisher", scene.Title);

        Press(overlay, scheduler, host, Key.Right);
        Assert.Equal("Vanisher", scene.Title);
        Assert.Equal(1, scheduler.Tick);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(2, scheduler.Tick);
        Assert.Equal("Populated", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Contains("Vanisher", scene.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone", "Walker", "Walker (1)"], Rows(scene));
        Assert.Equal(FirstEntity + 2, scene.FocusedIndex);
    }

    // The suffix is a place in scene order, so a panel's title drops one when an earlier entity
    // of its type leaves, matching the page beneath.
    [Fact]
    public void APanelsSuffix_FollowsThePageWhenAnEarlierEntityOfItsTypeLeaves()
    {
        using SceneHost host = CreateHost(new Departing());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal([.. Head, "Vanisher", "Lone", "Vanisher (1)"], Rows(scene));

        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Vanisher (1)", scene.Title);

        Press(overlay, scheduler, host, Key.Right);
        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(2, scheduler.Tick);
        Assert.Equal("Vanisher", scene.Title);
        Assert.Equal(3, scene.Depth);

        Press(overlay, scheduler, host, Key.Backspace);
        Assert.Equal([.. Head, "Lone", "Vanisher"], Rows(scene));
    }

    [Fact]
    public void ALoad_RefillsThePageFromTheNewSceneAndAnEmptySceneShowsThePlaceholder()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], Rows(scene));

        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.IsType<OtherScene>(host.Scene);
        Assert.Equal("Load Scene", scene.Title);

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal("OtherScene", scene.Title);
        Assert.Equal([.. Head, "Lone"], Rows(scene));

        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.Up);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.IsType<EmptyScene>(host.Scene);

        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal("EmptyScene", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Equal([.. Head, "<Nothing to show>"], Rows(scene));
    }

    // A panel open across a load: its entity's scene is gone, so it pops to the page, which is
    // refilled from the new scene.
    [Fact]
    public void ALoadWithAPanelOpen_PopsToTheRefilledPage()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Lone", scene.Title);

        overlay.Load(SceneTransition.ToScene(typeof(OtherScene), null));

        Assert.Equal("OtherScene", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Contains("Lone", scene.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone"], Rows(scene));
    }

    // A load from its submenu leaves the panel beneath stale; the frame whose Back exposes it
    // shows the page it pops to, never the departed entity's panel.
    [Fact]
    public void ABackThatExposesAStalePanel_PopsItOnTheSameFrame()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Lone", scene.Title);

        Press(overlay, scheduler, host, Key.L);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.IsType<OtherScene>(host.Scene);
        Assert.Equal("Load Scene", scene.Title);

        Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Backspace));

        Assert.Equal("OtherScene", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Contains("Lone", scene.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone"], Rows(scene));
    }

    // The engine's own Remove command on an entity: the tick after it takes the entity out, and
    // the panel pops as for any removed subject.
    [Fact]
    public void TheRemoveCommand_TakesTheEntityOutAndPopsItsPanel()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Walker", scene.Title);
        Assert.Equal("  Remove", scene.RowText(scene.FocusedIndex));

        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Populated", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Contains("Walker", scene.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone", "Vanisher", "Walker"], Rows(scene));
    }

    // Only a transition that fails to bring its scene up is a status-line matter; the tick's own
    // failure after a command is the game's crash, as it is from the Step row.
    [Fact]
    public void ACommandWhoseFollowingStepThrows_Propagates()
    {
        using SceneHost host = CreateHost(new Brittle());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Assert.Equal([.. Head[..6], "  (Commands)", "  Arm", "", "[Entities]", "<Nothing to show>"], Rows(scene));

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => Frame(overlay, scheduler, host, DeviceSnapshot.Of(Key.Enter)));

        Assert.Equal("armed", thrown.Message);
        Assert.Equal(string.Empty, scene.Status);
    }

    // The root hotkey fires at any depth: with the page already stacked it neither stacks a
    // second nor replaces the one that later steps refresh.
    [Fact]
    public void TheSceneKey_WithThePageAlreadyStacked_DoesNothing()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host, registry: CreateRegistry());
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Walker", scene.Title);
        Assert.Equal(3, scene.Depth);

        Press(overlay, scheduler, host, Key.S);

        Assert.Equal("Walker", scene.Title);
        Assert.Equal(3, scene.Depth);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal("Position  (11, 0)", scene.RowText(1));

        Press(overlay, scheduler, host, Key.L);
        Assert.Equal("Load Scene", scene.Title);
        Assert.Equal(4, scene.Depth);

        Press(overlay, scheduler, host, Key.S);

        Assert.Equal("Load Scene", scene.Title);
        Assert.Equal(4, scene.Depth);

        Press(overlay, scheduler, host, Key.Backspace);
        Press(overlay, scheduler, host, Key.Backspace);

        Assert.Equal("Populated", scene.Title);
        Assert.Equal(2, scene.Depth);
        Assert.Equal([.. Head, "Lone", "Walker", "Vanisher", "Walker (1)"], Rows(scene));
    }

    // Closed, the game runs; reopened, the panel shows where it got to, and a scene it replaced
    // pops the panel as a step would.
    [Fact]
    public void ReopeningAfterTheGameRan_RefreshesThePanelOrPopsItWithTheDepartedScene()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);
        Assert.Equal("Position  (10, 0)", scene.RowText(1));

        Press(overlay, scheduler, host, Key.Grave);
        Assert.False(overlay.IsOpen);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        long ran = scheduler.Tick;
        Assert.True(ran >= 2);

        Press(overlay, scheduler, host, Key.Grave);

        Assert.True(overlay.IsOpen);
        Assert.Equal("Walker", scene.Title);
        Assert.Equal($"Position  ({10 + ran}, 0)", scene.RowText(1));

        Press(overlay, scheduler, host, Key.Grave);
        host.Run.RequestScene<OtherScene>();
        Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        Assert.IsType<OtherScene>(host.Scene);

        Press(overlay, scheduler, host, Key.Grave);

        Assert.True(overlay.IsOpen);
        Assert.Equal("OtherScene", scene.Title);
        Assert.Contains("Walker", scene.Status, StringComparison.Ordinal);
        Assert.Equal([.. Head, "Lone"], Rows(scene));
    }

    // Sections are set apart by a blank row, and a component that writes nothing says so.
    [Fact]
    public void APanel_SeparatesComponentSectionsWithABlankRowAndNotesAnEmptyOne()
    {
        using SceneHost host = CreateHost();
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);
        OverlayScene scene = overlay.Scene;

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Down);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Vanisher", scene.Title);
        Assert.Equal(
            ["[Entity]", "Position", "ZIndex", "  (Commands)", "  Remove", "", "[Tag]", "Label", "", "[Mute]", "<Nothing to show>"],
            Rows(scene));
    }

    // Every row named rather than read: its heading, its command, or the field's name without the
    // column the panel pads it into. What a field reads is DebugPanelTests'.
    private static string[] Rows(OverlayScene scene)
    {
        string[] rows = Labels(scene);
        for (int index = 0; index < rows.Length; index++)
        {
            string row = rows[index];
            string named = row.TrimStart();
            int column = named.IndexOf("  ", StringComparison.Ordinal);

            rows[index] = row[..(row.Length - named.Length)] + (column < 0 ? named.TrimEnd() : named[..column]);
        }

        return rows;
    }

    private static SceneHost CreateHost(Scene? first = null) =>
        new(
            SceneTransition.ToScene(first?.GetType() ?? typeof(Populated), null),
            (in SceneTransition target) => target.SceneType switch
            {
                Type type when first is not null && type == first.GetType() => first,
                Type type when type == typeof(Populated) => new Populated(),
                Type type when type == typeof(OtherScene) => new OtherScene(),
                Type type when type == typeof(EmptyScene) => new EmptyScene(),
                Type type when type == typeof(PayloadScene) => new PayloadScene(),
                _ => throw new InvalidOperationException($"Unexpected transition {target.Kind}."),
            },
            new Run(new RandomSource(Seed)));

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

    // A scene with a watch, a command and a toggle of its own — the watch written last, so the
    // grouping is what puts it first — over one entity with a command.
    private sealed class Seamed : Scene
    {
        internal Seamed() => Add(new Nudger(new Vector2(1f, 2f)));

        internal int Spawned { get; set; }

        internal bool Slow { get; private set; }

        protected override void OnDebugPanel(DebugPanel panel)
        {
            panel.Command("Spawn", () => Spawned++);
            panel.Toggle("Slow", Slow, on => Slow = on);
            panel.Field("Spawned", Spawned);
        }
    }

    // A scene whose commands ask the run for a scene: one whose start fails, one that loads.
    private sealed class Requesting : Scene
    {
        internal Requesting() => Add(new Lone(Vector2.Zero));

        protected override void OnDebugPanel(DebugPanel panel)
        {
            panel.Command("Break", () => Run.RequestScene<PayloadScene>());
            panel.Command("Next", () => Run.RequestScene<OtherScene>());
        }
    }

    // Arm is a plain command; the step that follows it throws.
    private sealed class Brittle : Scene
    {
        private bool _armed;

        protected override void OnDebugPanel(DebugPanel panel) => panel.Command("Arm", () => _armed = true);

        protected override void OnStep(in StepContext context)
        {
            if (_armed)
            {
                throw new InvalidOperationException("armed");
            }
        }
    }

    private sealed class PayloadScene : Scene
    {
        protected override void OnStart()
        {
            if (EntryPayload is null)
            {
                throw new InvalidOperationException("PayloadScene needs a payload.");
            }
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

        protected internal override void OnDebugPanel(DebugPanel panel) =>
            panel.Field("Name", "solitary");
    }

    private sealed class Nudger(Vector2 position) : Entity(position)
    {
        protected internal override void OnDebugPanel(DebugPanel panel) =>
            panel.Command("Nudge", () => Position += Vector2.UnitX);
    }

    private sealed class Tag(string label) : Component
    {
        protected internal override void OnDebugPanel(DebugPanel panel) =>
            panel.Field("Label", label);
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
