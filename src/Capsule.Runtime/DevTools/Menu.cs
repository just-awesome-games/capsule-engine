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

    // Empty until filled, which the one caller does before the menu is shown.
    private Menu(string? title)
    {
        Title = title;
        Items = [];
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

    // The Debug Draw submenu, filled from the channels that have emitted so far; at least one has.
    internal static Menu DebugDraw(OverlayHost overlay)
    {
        Menu menu = new("Debug Draw");
        menu.FillDebugDraw(overlay);

        return menu;
    }

    // Refills this menu with a row per channel, its label carrying the channel's state.
    internal void FillDebugDraw(OverlayHost overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        string[] channels = overlay.Channels;
        List<MenuItem> items = new(channels.Length);
        foreach (string channel in channels)
        {
            string label = (overlay.IsChannelEnabled(channel) ? "[x] " : "[ ] ") + channel;
            items.Add(new MenuItem(label, () => overlay.ToggleChannel(channel)));
        }

        Items = Require(items);
    }

    // The Time Scale submenu, one row per host pace on the ladder.
    internal static Menu TimeScale(OverlayHost overlay)
    {
        Menu menu = new("Time Scale");
        menu.FillTimeScale(overlay);

        return menu;
    }

    // Refills this menu with a row per pace, marked where it is the one in force; exactly one is.
    internal void FillTimeScale(OverlayHost overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        (double Scale, string Label)[] paces = OverlayHost.TimeScales;
        List<MenuItem> items = new(paces.Length);
        foreach ((double scale, string label) in paces)
        {
            string row = (overlay.IsTimeScale(scale) ? "(x) " : "( ) ") + label;
            items.Add(new MenuItem(row, () => overlay.SetTimeScale(scale)));
        }

        Items = Require(items);
    }

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
