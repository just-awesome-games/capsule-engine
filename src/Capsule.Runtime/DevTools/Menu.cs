using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// One navigable list on the overlay's stack — its items in order under an optional title, with
// the focus and window it remembers — and nothing of how it is drawn.
internal sealed class Menu
{
    internal Menu(string? title, IReadOnlyList<MenuItem> items)
    {
        Title = title;
        Items = Require(items);
    }

    internal string? Title { get; }

    internal IReadOnlyList<MenuItem> Items { get; private set; }

    internal int Focus { get; set; }

    internal int First { get; set; }

    // Scene actions need a run of scenes; loading needs a registry with something in it.
    internal static Menu Default(OverlayHost overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        List<MenuItem> items = [];

        if (overlay.HasScenes)
        {
            items.Add(new MenuItem("Scene", overlay.OpenScenePage, OverlayActions.ScenePage));
        }

        items.Add(new MenuItem("Step", overlay.StepGame, OverlayActions.Step, Repeats: true));
        items.Add(new MenuItem("Debug Draw", overlay.OpenDebugDraw, OverlayActions.DebugDraw));
        items.Add(new MenuItem("Time Scale", overlay.OpenTimeScale, OverlayActions.TimeScale));

        if (overlay.HasScenes)
        {
            items.Add(new MenuItem("Restart", overlay.Restart, OverlayActions.Restart));

            // Built once: the registry is fixed for the run, and the menu keeps its focus between opens.
            if (overlay.Registrations.Count > 0)
            {
                Menu sceneMenu = LoadScene(overlay);
                items.Add(new MenuItem("Load Scene", () => overlay.Scene.Push(sceneMenu), OverlayActions.LoadScene));
            }
        }

        items.Add(new MenuItem("Frame Pane", overlay.ToggleFramePane, OverlayActions.FramePane));
        items.Add(new MenuItem("Hide", overlay.Hide, OverlayActions.Hide));

        if (overlay.HasScenes)
        {
            items.Add(new MenuItem("Exit", overlay.Exit, OverlayActions.Exit));
        }

        return new Menu(null, items);
    }

    // Every registered class by name. A document-backed class is requested by its document's name,
    // which is the form the registry composes it from.
    internal static Menu LoadScene(OverlayHost overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        List<SceneRegistration> registrations = [.. overlay.Registrations];
        registrations.Sort(static (a, b) => string.CompareOrdinal(a.SceneType.Name, b.SceneType.Name));

        List<MenuItem> items = new(registrations.Count);
        foreach (SceneRegistration registration in registrations)
        {
            SceneTransition target = registration.DocumentName is { } name
                ? SceneTransition.ToName(name, null)
                : SceneTransition.ToScene(registration.SceneType, null);
            items.Add(new MenuItem(registration.SceneType.Name, () => overlay.Load(in target)));
        }

        return new Menu("Load Scene", items);
    }

    // Rewrites every row, for a submenu whose labels carry a mark the host flips: the menu keeps
    // its focus and its identity, so the row under the cursor stays under it.
    internal void Fill(IReadOnlyList<MenuItem> rows) => Items = Require(rows);

    private static IReadOnlyList<MenuItem> Require(IReadOnlyList<MenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            throw new ArgumentException("A debug menu holds at least one item.", nameof(items));
        }

        return items;
    }
}
