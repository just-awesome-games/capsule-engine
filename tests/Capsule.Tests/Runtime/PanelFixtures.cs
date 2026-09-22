using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Runtime;

// The scenes the overlay's panel specs walk: entities that move, leave, nest and carry hooks.
internal static class PanelFixtures
{
    internal const ulong Seed = 42;

    // The page's head for a scene with no hook of its own: the Scene section holding the engine's own
    // rows, then the Entities heading. The first entity row is at index FirstEntity. Rows are named
    // rather than spelt out; what each one reads is DebugPanelTests' to hold.
    internal static readonly string[] Head =
    [
        "[Scene]",
        "Seed",
        "Size",
        "ClearColor",
        "Ambient",
        "Sampling",
        "Camera",
        "Camera Type",
        "Camera Viewport",
        "",
        "[Entities]",
    ];

    internal const int FirstEntity = 11;

    // Every row named rather than read: its heading, its command, or the field's name without the
    // column the panel pads it into.
    internal static string[] Named(OverlayHost overlay)
    {
        string[] rows = OverlayRig.Rows(overlay);
        for (int index = 0; index < rows.Length; index++)
        {
            string row = rows[index];
            string named = row.TrimStart();
            int column = named.IndexOf("  ", StringComparison.Ordinal);

            rows[index] = row[..(row.Length - named.Length)] + (column < 0 ? named.TrimEnd() : named[..column]);
        }

        return rows;
    }

    // One row as drawn, hotkey column and all.
    internal static string Drawn(OverlayHost overlay, int row) => overlay.Scene.ShownRows()[row];

    internal static SceneHost CreateHost(Scene? first = null) =>
        new(
            SceneTransition.ToScene(first?.GetType() ?? typeof(Populated), null),
            (in SceneTransition target) => target.SceneType switch
            {
                Type type when first is not null && type == first.GetType() => first,
                Type type when type == typeof(Populated) => new Populated(),
                Type type when type == typeof(OtherScene) => new OtherScene(),
                Type type when type == typeof(EmptyScene) => new EmptyScene(),
                Type type when type == typeof(BrokenScene) => new BrokenScene(),
                _ => throw new InvalidOperationException($"Unexpected transition {target.Kind}."),
            },
            new Run(new RandomSource(Seed)));

    internal static SceneRegistry CreateRegistry() =>
        new(
            new EntityRegistry([]),
            [
                SceneRegistration.Plain(typeof(EmptyScene), static _ => new EmptyScene()),
                SceneRegistration.Plain(typeof(OtherScene), static _ => new OtherScene()),
            ]);

    // One Lone, one Vanisher that leaves on the second tick, two Walkers a unit apart in scene order.
    internal sealed class Populated : Scene
    {
        internal Populated()
        {
            Add(new Lone(new Vector2(5f, 6f)) { ZIndex = 3 });
            Add(new Walker(new Vector2(10f, 0f)));
            Add(new Vanisher(Vector2.Zero));
            Add(new Walker(new Vector2(20f, 0f)));
        }
    }

    // A Lone, then a Walker placing a named child with two children of its own, an unnamed child and a
    // second child named the same as the first.
    internal sealed class Nested : Scene
    {
        internal Nested()
        {
            Add(new Lone(new Vector2(5f, 6f)));
            Walker root = new(new Vector2(10f, 0f));
            Entity spark = new(root, new Vector2(1f, 2f)) { Name = "Spark" };
            _ = new Entity(spark, new Vector2(0f, 0f));
            _ = new Entity(spark, new Vector2(1f, 1f));
            _ = new Entity(root, new Vector2(3f, 3f));
            _ = new Entity(root, new Vector2(4f, 0f)) { Name = "Spark" };
            Add(root);
        }
    }

    // A Walker whose only child leaves on the first step.
    internal sealed class Parented : Scene
    {
        internal Parented()
        {
            Walker root = new(new Vector2(10f, 0f));
            _ = new Vanisher(root);
            Add(root);
        }
    }

    // Two Vanishers around a Lone; only the first leaves.
    internal sealed class Departing : Scene
    {
        internal Departing()
        {
            Add(new Vanisher(Vector2.Zero));
            Add(new Lone(Vector2.Zero));
            Add(new Vanisher(Vector2.Zero, leavesOn: long.MaxValue));
        }
    }

    // A scene with a watch, a command and a toggle of its own, the watch written last so the grouping
    // is what puts it first, over one entity with a command.
    internal sealed class Seamed : Scene
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
    internal sealed class Requesting : Scene
    {
        internal Requesting() => Add(new Lone(Vector2.Zero));

        protected override void OnDebugPanel(DebugPanel panel)
        {
            panel.Command("Break", () => Run.RequestScene<BrokenScene>());
            panel.Command("Next", () => Run.RequestScene<OtherScene>());
        }
    }

    // Arm is a plain command; the step that follows it throws.
    internal sealed class Brittle : Scene
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

    // Refuses to come up at all, which is how a load that fails mid-tick is provoked.
    internal sealed class BrokenScene : Scene
    {
        protected override void OnStart() => throw new InvalidOperationException("BrokenScene never starts.");
    }

    internal sealed class OtherScene : Scene
    {
        internal OtherScene() => Add(new Lone(Vector2.Zero));
    }

    internal sealed class EmptyScene : Scene;

    internal sealed class Lone : Entity
    {
        internal Lone(Vector2 position)
            : base(position) => Add(new Tag("one"));

        protected internal override void OnDebugPanel(DebugPanel panel) => panel.Field("Name", "solitary");
    }

    internal sealed class Nudger(Vector2 position) : Entity(position)
    {
        protected internal override void OnDebugPanel(DebugPanel panel) =>
            panel.Command("Nudge", () => Position += Vector2.UnitX);
    }

    internal sealed class Tag(string label) : Component
    {
        protected internal override void OnDebugPanel(DebugPanel panel) => panel.Field("Label", label);
    }

    internal sealed class Walker(Vector2 position) : Entity(position)
    {
        protected internal override void OnStep(in StepContext context) => Position += Vector2.UnitX;
    }

    internal sealed class Mute : Component;

    internal sealed class Vanisher : Entity
    {
        private readonly long _leavesOn;

        internal Vanisher(Vector2 position, long leavesOn = 1)
            : base(position)
        {
            _leavesOn = leavesOn;
            Add(new Tag("v"));
            Add(new Mute());
        }

        internal Vanisher(Entity parent)
            : base(parent) => _leavesOn = 1;

        protected internal override void OnStep(in StepContext context)
        {
            if (context.Tick >= _leavesOn)
            {
                Scene.Remove(this);
            }
        }
    }
}
