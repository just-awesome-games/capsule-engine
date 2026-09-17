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
    private const string ParentLabel = "Parent";

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

    // Every entity panel on the stack, bottom first: one per entity opened from the page, and one
    // more for each parent crawled up to from a child's panel. Whichever is current is the one a
    // refresh rebuilds; those backed out of are dropped at the next refresh.
    private readonly List<OpenedPanel> _entityPanels = [];

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
            int open = _entityPanels.Count - 1;
            while (open >= 0 && !_scene.Contains(_entityPanels[open].Menu))
            {
                _entityPanels.RemoveAt(open--);
            }

            if (open >= 0 && _entityPanels[open] is { } top && ReferenceEquals(current, top.Menu) && top.Generation != _generation)
            {
                if (PanelMenu(top.Subject) is { } panel)
                {
                    _entityPanels[open] = new OpenedPanel(panel, top.Subject, _generation);
                    _scene.Replace(panel);

                    return true;
                }

                _entityPanels.RemoveAt(open);
                _scene.Pop();
                note ??= $"{top.Menu.Title} left the scene";
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
        Menu panel = PanelMenu(entity)!;
        _entityPanels.Add(new OpenedPanel(panel, entity, _generation));
        _scene.Push(panel);
    }

    // The scene's walk as rows, a blank row, the Entities heading, then a row per entity in scene
    // order — tree order, each indented by its depth so a subtree reads under its root — each
    // named as Label names it, or the note where the scene holds none.
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

        foreach (Entity entity in entities)
        {
            string label = Label(entity);
            for (Entity? above = entity.Parent; above is not null; above = above.Parent)
            {
                label = Indent + label;
            }

            items.Add(new MenuItem(label, () => OpenPanel(entity)));
        }

        return new Menu(scene.GetType().Name, items);
    }

    // The entity's Name, or its type name where it has none, suffixed by how many of its siblings
    // reading the same came before it when any did — Enemy, Enemy (1), Enemy (2): roots count
    // among the scene's roots in scene order, children among their parent's children, so each
    // layer of the tree reads on its own.
    private static string Label(Entity entity)
    {
        string name = NameOf(entity);
        int before = 0;
        ReadOnlySpan<Entity> peers = entity.Parent is { } parent ? parent.Children : entity.Scene!.Entities;
        foreach (Entity peer in peers)
        {
            if (ReferenceEquals(peer, entity))
            {
                break;
            }

            if (ReferenceEquals(peer.Parent, entity.Parent) && NameOf(peer) == name)
            {
                before++;
            }
        }

        return before == 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{name} ({before})");
    }

    private static string NameOf(Entity entity) => entity.Name ?? entity.GetType().Name;

    // The entity's walk as rows. Null once the entity is no longer in the held scene. The title
    // is the entity's label as the page beneath now names it, so it follows a sibling's leaving.
    // Under a parent, a Parent row heads the Entity section: focusable, and chosen it pushes the
    // parent's own panel — navigation over the tree, which steps nothing — labelled as the page
    // names the parent. The overlay's row, over Entity.Parent, and no hook of the engine's.
    private Menu? PanelMenu(Entity entity)
    {
        Scene scene = _scenes.Scene;
        if (!ReferenceEquals(entity.Scene, scene))
        {
            return null;
        }

        _panel.Clear();
        entity.RunDebugPanel(_panel);

        List<MenuItem> items = new(_panel.Rows.Length + 1);
        AddRows(items, _panel.Rows);

        if (entity.Parent is { } parent)
        {
            // Under the Entity heading, ahead of the fields, in the fields' own column.
            items.Insert(1, new MenuItem(ParentLabel.PadRight(ColumnOf(_panel.Rows)) + Label(parent), () => OpenPanel(parent)));
        }

        return new Menu(Label(entity), items);
    }

    // The widest field label of a page plus the gap, which is where every value starts.
    private static int ColumnOf(ReadOnlySpan<DebugPanelRow> rows)
    {
        int column = 0;
        foreach (DebugPanelRow row in rows)
        {
            if (row.Kind == DebugPanelRowKind.Field)
            {
                column = Math.Max(column, row.Label.Length);
            }
        }

        return column + ValueGap;
    }

    // A panel's rows as items, section by section: the heading in brackets, the section's fields
    // as label padded to a shared column then value, then — only where the section wrote any —
    // the Commands sub-heading and its commands and toggles in write order, a toggle as its
    // state then its label. Only a command or toggle is focused; the rest are read. A blank row
    // before each section but the first, and the note in a section with nothing at all.
    private void AddRows(List<MenuItem> items, ReadOnlySpan<DebugPanelRow> rows)
    {
        int column = ColumnOf(rows);

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

    // One entity panel on the stack: the menu as last built, whose it is, and the tick count it
    // was built under.
    private readonly record struct OpenedPanel(Menu Menu, Entity Subject, int Generation);

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
