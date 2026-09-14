using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// An ordered list of items under a title, which the default menu has none of. Remembers its
// focused item so a pop lands where the opener was.
internal sealed class DebugMenu
{
    internal DebugMenu(string? title, IReadOnlyList<DebugMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            throw new ArgumentException("A debug menu holds at least one item.", nameof(items));
        }

        Title = title;
        Items = items;
    }

    internal string? Title { get; }

    internal IReadOnlyList<DebugMenuItem> Items { get; }

    internal int Focus { get; set; }

    // Scene actions need a run of scenes; loading needs a registry with something in it.
    internal static DebugMenu Default(DebugOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        List<DebugMenuItem> items = [new("Step", overlay.StepGame, DebugInput.Step, Repeats: true)];

        if (overlay.HasScenes)
        {
            items.Add(new DebugMenuItem("Restart", overlay.Restart, DebugInput.Restart));

            // Built once: the registry is fixed for the run, and the menu keeps its focus between opens.
            if (overlay.Registrations.Count > 0)
            {
                DebugMenu sceneMenu = LoadScene(overlay);
                items.Add(new DebugMenuItem("Load Scene", () => overlay.Scene.Push(sceneMenu)));
            }
        }

        items.Add(new DebugMenuItem("Hide", overlay.Hide, DebugInput.Hide));

        if (overlay.HasScenes)
        {
            items.Add(new DebugMenuItem("Exit", overlay.Exit));
        }

        return new DebugMenu(null, items);
    }

    // Every registered class by name. A document-backed class is requested by its document's name,
    // which is the form the registry composes it from.
    internal static DebugMenu LoadScene(DebugOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);

        List<SceneRegistration> registrations = [.. overlay.Registrations];
        registrations.Sort(static (a, b) => string.CompareOrdinal(a.SceneType.Name, b.SceneType.Name));

        List<DebugMenuItem> items = new(registrations.Count);
        foreach (SceneRegistration registration in registrations)
        {
            SceneTransition target = registration.DocumentName is { } name
                ? SceneTransition.ToName(name, null)
                : SceneTransition.ToScene(registration.SceneType, null);
            items.Add(new DebugMenuItem(registration.SceneType.Name, () => overlay.Load(in target)));
        }

        return new DebugMenu("Load Scene", items);
    }
}
