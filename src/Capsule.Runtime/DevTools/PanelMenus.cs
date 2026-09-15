using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// Builds the overlay's menus from DebugPanel rows — the held scene's page and one entity's panel,
// each rebuilt in place once stale — and runs their commands; nothing of the rows' drawing, which
// is the menu's.
internal sealed class PanelMenus
{
    private const string EntitiesHeading = "[Entities]";
    private const string EmptySection = "<Nothing to show>";

    // The sub-heading over a section's commands and toggles, and the indent that sets it and
    // them under the section's heading.
    private const string Indent = "  ";
    private const string CommandsHeading = Indent + "(Commands)";

    // Spaces between the widest label of a page and its value column.
    private const int ValueGap = 2;

    private readonly OverlayScene _scene;
    private readonly SceneHost _scenes;
    private readonly Action<Action> _tick;
    private readonly DebugPanel _panel = new();

    // Counts the stepped ticks; a menu built under an earlier count is stale.
    private int _generation;

    private Menu? _page;
    private int _pageGeneration;
    private Menu? _entityPanel;
    private Entity? _subject;
    private string _subjectTitle = string.Empty;
    private int _entityPanelGeneration;

    // `tick` runs one stepped tick with the command or toggle inside it and rebuilds the pages.
    internal PanelMenus(OverlayScene scene, SceneHost scenes, Action<Action> tick)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(tick);

        _scene = scene;
        _scenes = scenes;
        _tick = tick;
    }

    // Pushes the scene page. The hotkey fires at any depth: while the page is already open —
    // current, or beneath a panel or another submenu — it does nothing, so the one tracked page
    // is never orphaned under a second.
    internal void Open()
    {
        if (_page is { } open && _scene.Contains(open))
        {
            return;
        }

        _page = PageMenu();
        _pageGeneration = _generation;
        _scene.Push(_page);
    }

    // The scene stepped, or ran while the overlay was closed, so every menu built before now is
    // stale.
    internal void Invalidate() => _generation++;

    // Rebuilds the current menu, if it is one of the two and stale, from the scene as it now
    // stands. A subject the step removed — or a load replaced along with its scene — pops to the
    // page beneath, which is then rebuilt in its turn; the status line names the subject.
    // Returns whether the stack changed.
    internal bool Refresh()
    {
        string? note = null;
        try
        {
            return RefreshCurrent(ref note);
        }
        finally
        {
            if (note is not null)
            {
                _scene.SetStatus(note);
            }
        }
    }

    private bool RefreshCurrent(ref string? note)
    {
        bool changed = false;
        while (true)
        {
            Menu current = _scene.Current;

            if (ReferenceEquals(current, _entityPanel) && _entityPanelGeneration != _generation)
            {
                if (PanelMenu(_subject!) is { } panel)
                {
                    _entityPanel = panel;
                    _entityPanelGeneration = _generation;
                    _scene.Replace(panel);

                    return true;
                }

                _entityPanel = null;
                _scene.Pop();
                note ??= $"{_subjectTitle} left the scene";
                changed = true;
            }
            else if (ReferenceEquals(current, _page) && _pageGeneration != _generation)
            {
                _page = PageMenu();
                _pageGeneration = _generation;
                _scene.Replace(_page);

                return true;
            }
            else
            {
                return changed;
            }
        }
    }

    // A command or toggle from a hook is part of the tick it forces: it runs inside that step,
    // ahead of the scene's own work, so its sounds and transitions are consumed with the step, and
    // every page is rebuilt after. What the action throws is the developer's, and is not caught.
    private void Run(Action activate) => _tick(activate);

    private void OpenPanel(Entity entity)
    {
        _subject = entity;
        _entityPanel = PanelMenu(entity)!;
        _entityPanelGeneration = _generation;
        _scene.Push(_entityPanel);
    }

    // The scene's walk as rows, a blank row, the Entities heading, then a row per entity in scene
    // order, each named as Label names it — or the note where the scene holds none.
    private Menu PageMenu()
    {
        Scene scene = _scenes.Scene;
        _panel.Clear();
        scene.RunDebugPanel(_panel);

        ReadOnlySpan<Entity> entities = scene.Entities;
        List<MenuItem> items = new(_panel.Rows.Length + entities.Length + 2);
        AddRows(items, _panel.Rows);
        items.Add(new MenuItem(string.Empty, null));
        items.Add(new MenuItem(EntitiesHeading, null));

        if (entities.IsEmpty)
        {
            items.Add(new MenuItem(EmptySection, null));
        }

        Dictionary<Type, int> seen = [];
        foreach (Entity entity in entities)
        {
            items.Add(new MenuItem(Label(entity, seen), () => OpenPanel(entity)));
        }

        return new Menu(scene.GetType().Name, items);
    }

    // The entity's type name, suffixed by how many of its type came before it in scene order
    // when any did — Enemy, Enemy (1), Enemy (2); `seen` carries that count from one entity of the
    // walk to the next.
    private static string Label(Entity entity, Dictionary<Type, int> seen)
    {
        Type type = entity.GetType();
        int before = seen.TryGetValue(type, out int count) ? count : 0;
        seen[type] = before + 1;

        return before == 0 ? type.Name : string.Create(CultureInfo.InvariantCulture, $"{type.Name} ({before})");
    }

    // The entity's walk as rows. Null once the entity is no longer in the held scene. The title
    // is the entity's label as the page beneath now names it, so it follows a sibling's leaving.
    private Menu? PanelMenu(Entity entity)
    {
        Scene scene = _scenes.Scene;
        if (!ReferenceEquals(entity.Scene, scene))
        {
            return null;
        }

        Dictionary<Type, int> seen = [];
        foreach (Entity candidate in scene.Entities)
        {
            string label = Label(candidate, seen);
            if (ReferenceEquals(candidate, entity))
            {
                _subjectTitle = label;
                break;
            }
        }

        _panel.Clear();
        entity.RunDebugPanel(_panel);

        List<MenuItem> items = new(_panel.Rows.Length);
        AddRows(items, _panel.Rows);

        return new Menu(_subjectTitle, items);
    }

    // A panel's rows as items, section by section: the heading in brackets, the section's fields
    // as label padded to a shared column then value, then — only where the section wrote any —
    // the Commands sub-heading and its commands and toggles in write order, a toggle as its
    // state then its label. Only a command or toggle is focused; the rest are read. A blank row
    // before each section but the first, and the note in a section with nothing at all.
    private void AddRows(List<MenuItem> items, ReadOnlySpan<DebugPanelRow> rows)
    {
        int column = 0;
        foreach (DebugPanelRow row in rows)
        {
            if (row.Kind == DebugPanelRowKind.Field)
            {
                column = Math.Max(column, row.Label.Length);
            }
        }

        column += ValueGap;

        int index = 0;
        bool first = true;
        while (index < rows.Length)
        {
            if (!first)
            {
                items.Add(new MenuItem(string.Empty, null));
            }

            first = false;
            if (rows[index].IsHeading)
            {
                items.Add(new MenuItem($"[{rows[index].Label}]", null));
                index++;
            }

            int end = index;
            while (end < rows.Length && !rows[end].IsHeading)
            {
                end++;
            }

            AddSection(items, rows[index..end], column);
            index = end;
        }
    }

    private void AddSection(List<MenuItem> items, ReadOnlySpan<DebugPanelRow> rows, int column)
    {
        bool anyCommand = false;
        foreach (DebugPanelRow row in rows)
        {
            if (row.Kind == DebugPanelRowKind.Field)
            {
                items.Add(new MenuItem(row.Label.PadRight(column) + row.Value, null));
            }
            else
            {
                anyCommand = true;
            }
        }

        if (!anyCommand)
        {
            if (rows.IsEmpty)
            {
                items.Add(new MenuItem(EmptySection, null));
            }

            return;
        }

        items.Add(new MenuItem(CommandsHeading, null));
        foreach (DebugPanelRow row in rows)
        {
            if (row.Kind == DebugPanelRowKind.Field)
            {
                continue;
            }

            Action activate = row.Activate!;
            string label = row.Kind == DebugPanelRowKind.Command ? row.Label : (row.On ? "[x] " : "[ ] ") + row.Label;
            items.Add(new MenuItem(Indent + label, () => Run(activate)));
        }
    }
}
