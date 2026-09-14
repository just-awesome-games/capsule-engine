using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// The Inspect menus over the held scene: the entities it holds, in scene order, and one entity's
// state as its inspect walk reports it. Each is a menu on the debug scene's stack, built from the
// scene as it stands and rebuilt in place once it is current after a stepped tick, so a step shows
// what it changed; a panel whose entity the step took away pops to the list beneath it. An entity
// is named by its type, the second and later of a type suffixed by their place among it in scene
// order — Enemy, Enemy (1), Enemy (2) — and a panel's entity is held by reference.
internal sealed class DebugInspection
{
    private const string ListTitle = "Inspect";
    private const string EmptySection = "<Nothing to inspect>";

    // Spaces between the widest label of a panel and its value column.
    private const int ValueGap = 2;

    private readonly DebugScene _scene;
    private readonly SceneHost _scenes;
    private readonly Inspector _inspector = new();

    // Counts the stepped ticks; a menu built under an earlier count is stale.
    private int _generation;

    private DebugMenu? _list;
    private int _listGeneration;
    private DebugMenu? _panel;
    private Entity? _subject;
    private string _subjectTitle = string.Empty;
    private int _panelGeneration;

    internal DebugInspection(DebugScene scene, SceneHost scenes)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(scenes);

        _scene = scene;
        _scenes = scenes;
    }

    // Pushes the entity list; with nothing in the scene there is nothing to list, so the status
    // line says so instead. The hotkey fires at any depth: while the list is already open —
    // current, or beneath a panel or another submenu — it does nothing, so the one tracked list
    // is never orphaned under a second.
    internal void Open()
    {
        if (_list is { } open && _scene.Contains(open))
        {
            return;
        }

        if (ListMenu() is not { } list)
        {
            _scene.SetStatus("No entities in the scene");

            return;
        }

        _list = list;
        _listGeneration = _generation;
        _scene.Push(list);
    }

    // The scene stepped, or ran while the overlay was closed, so every menu built before now is
    // stale.
    internal void Invalidate() => _generation++;

    // Rebuilds the current menu, if it is one of the two and stale, from the scene as it now
    // stands. A subject the step removed — or a load replaced along with its scene — pops to the
    // list beneath, which is then rebuilt in its turn; the status line names the first subject
    // that went, the more specific of a cascade.
    internal void Refresh()
    {
        string? note = null;
        try
        {
            RefreshCurrent(ref note);
        }
        finally
        {
            if (note is not null)
            {
                _scene.SetStatus(note);
            }
        }
    }

    private void RefreshCurrent(ref string? note)
    {
        while (true)
        {
            DebugMenu current = _scene.Menu;

            if (ReferenceEquals(current, _panel) && _panelGeneration != _generation)
            {
                if (PanelMenu(_subject!) is { } panel)
                {
                    _panel = panel;
                    _panelGeneration = _generation;
                    _scene.Replace(panel);

                    return;
                }

                _panel = null;
                _scene.Pop();
                note ??= $"{_subjectTitle} left the scene";
            }
            else if (ReferenceEquals(current, _list) && _listGeneration != _generation)
            {
                if (ListMenu() is { } list)
                {
                    _list = list;
                    _listGeneration = _generation;
                    _scene.Replace(list);
                }
                else
                {
                    _list = null;
                    _scene.Pop();
                    note ??= "No entities in the scene";
                }

                return;
            }
            else
            {
                return;
            }
        }
    }

    private void OpenPanel(Entity entity)
    {
        _subject = entity;
        _panel = PanelMenu(entity)!;
        _panelGeneration = _generation;
        _scene.Push(_panel);
    }

    // A row per entity in scene order, each named as Label names it; null for an empty scene.
    private DebugMenu? ListMenu()
    {
        ReadOnlySpan<Entity> entities = _scenes.Scene.Entities;
        if (entities.IsEmpty)
        {
            return null;
        }

        Dictionary<Type, int> seen = [];
        List<DebugMenuItem> items = new(entities.Length);
        foreach (Entity entity in entities)
        {
            items.Add(new DebugMenuItem(Label(entity, seen), () => OpenPanel(entity)));
        }

        return new DebugMenu(ListTitle, items);
    }

    // The entity's type name, suffixed by how many of its type came before it in scene order
    // when any did; `seen` carries that count from one entity of the walk to the next.
    private static string Label(Entity entity, Dictionary<Type, int> seen)
    {
        Type type = entity.GetType();
        int before = seen.TryGetValue(type, out int count) ? count : 0;
        seen[type] = before + 1;

        return before == 0 ? type.Name : string.Create(CultureInfo.InvariantCulture, $"{type.Name} ({before})");
    }

    // The entity's inspect walk as rows: a heading row per component in brackets, a field row as
    // its label padded to a shared column then its value. The highlight is a reading cursor, so a
    // row does nothing when pressed; a blank row before each heading but the first and the note
    // under a heading with no fields are not even focused. Null once the entity is no longer in
    // the held scene. The title is the entity's label as the list beneath now names it, so it
    // follows a sibling's leaving.
    private DebugMenu? PanelMenu(Entity entity)
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

        _inspector.Clear();
        entity.RunInspect(_inspector);
        ReadOnlySpan<InspectorRow> rows = _inspector.Rows;

        int column = 0;
        foreach (InspectorRow row in rows)
        {
            if (!row.IsHeading)
            {
                column = Math.Max(column, row.Label.Length);
            }
        }

        column += ValueGap;

        List<DebugMenuItem> items = new(rows.Length);
        bool underHeading = false;
        bool headingEmpty = false;
        foreach (InspectorRow row in rows)
        {
            if (row.IsHeading)
            {
                if (underHeading)
                {
                    if (headingEmpty)
                    {
                        items.Add(new DebugMenuItem(EmptySection, null));
                    }

                    items.Add(new DebugMenuItem(string.Empty, null));
                }

                items.Add(new DebugMenuItem($"[{row.Label}]", static () => { }));
                underHeading = true;
                headingEmpty = true;
            }
            else
            {
                items.Add(new DebugMenuItem(row.Label.PadRight(column) + row.Value, static () => { }));
                headingEmpty = false;
            }
        }

        if (underHeading && headingEmpty)
        {
            items.Add(new DebugMenuItem(EmptySection, null));
        }

        return new DebugMenu(_subjectTitle, items);
    }
}
