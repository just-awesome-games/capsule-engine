using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// Builds the overlay's rows from DebugPanel hooks: the held scene's page and one entity's panel. The
// scene does the drawing.
internal sealed class PanelRows
{
    private const string EntitiesHeading = "[Entities]";
    private const string EmptySection = "<Nothing to show>";
    private const string ParentLabel = "Parent";

    // The sub-heading over a section's commands and toggles, and the indent that sets both under the
    // section's heading.
    private const string Indent = "  ";
    private const string CommandsHeading = Indent + "(Commands)";

    // Spaces between the widest field label of a page and its value column.
    private const int ValueGap = 2;

    private readonly SceneHost _scenes;
    private readonly Action<Action> _tick;
    private readonly Action<Entity> _open;
    private readonly DebugPanel _panel = new();

    // `tick` runs one stepped tick with a command or toggle inside it. `open` pushes an entity's panel
    // and steps nothing.
    internal PanelRows(SceneHost scenes, Action<Action> tick, Action<Entity> open)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(open);

        _scenes = scenes;
        _tick = tick;
        _open = open;
    }

    // Whether entity is still in the held scene, which keeps its panel on the stack.
    internal bool Holds(Entity entity) => ReferenceEquals(entity.SceneOrNull, _scenes.Scene);

    // The scene's own walk as rows, a blank row, the Entities heading, then a row per entity in scene
    // order, each indented by its depth to read a subtree under its root. Returns the page's title.
    internal string ScenePage(List<OverlayRow> rows)
    {
        Scene scene = _scenes.Scene;
        _panel.Clear();
        scene.RunDebugPanel(_panel);
        AddRows(rows, _panel.Rows);

        rows.Add(new OverlayRow(string.Empty, null));
        rows.Add(new OverlayRow(EntitiesHeading, null));

        ReadOnlySpan<Entity> entities = scene.Entities;
        if (entities.IsEmpty)
        {
            rows.Add(new OverlayRow(EmptySection, null));
        }

        foreach (Entity entity in entities)
        {
            string label = Label(entity);
            for (Entity? above = entity.Parent; above is not null; above = above.Parent)
            {
                label = Indent + label;
            }

            Entity opened = entity;
            rows.Add(new OverlayRow(label, () => _open(opened)));
        }

        return scene.GetType().Name;
    }

    // The entity's walk as rows, titled by its label as the page beneath names it. Under a parent, a
    // Parent row heads the Entity section. Choosing it opens the parent's own panel, which navigates
    // the tree and steps nothing.
    internal string EntityPanel(Entity entity, List<OverlayRow> rows)
    {
        _panel.Clear();
        entity.RunDebugPanel(_panel);
        AddRows(rows, _panel.Rows);

        if (entity.Parent is { } parent)
        {
            // Under the Entity heading, ahead of the fields, in the fields' own column.
            rows.Insert(1, new OverlayRow(ParentLabel.PadRight(ColumnOf(_panel.Rows)) + Label(parent), () => _open(parent)));
        }

        return Label(entity);
    }

    // The entity's Name, or its type name where it has none, suffixed by how many siblings reading the
    // same came before it: Enemy, Enemy (1), Enemy (2). Roots count among the scene's roots in scene
    // order and children among their parent's children, so each layer of the tree counts separately.
    internal static string Label(Entity entity)
    {
        string name = NameOf(entity);
        int before = 0;
        ReadOnlySpan<Entity> peers = entity.Parent is { } parent ? parent.Children : entity.Scene.Entities;
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

    // The widest field label of a page plus the gap, where every value starts.
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

    // A panel's rows section by section: the heading in brackets, the section's fields as label padded
    // to a shared column then value, then, where the section wrote any, the Commands sub-heading and
    // its commands and toggles in write order, a toggle as its state then its label. Only a command or
    // toggle is focusable. A blank row precedes each section but the first, and an empty section gets
    // the note.
    private void AddRows(List<OverlayRow> rows, ReadOnlySpan<DebugPanelRow> panel)
    {
        int column = ColumnOf(panel);

        int index = 0;
        bool first = true;
        while (index < panel.Length)
        {
            if (!first)
            {
                rows.Add(new OverlayRow(string.Empty, null));
            }

            first = false;
            if (panel[index].IsHeading)
            {
                rows.Add(new OverlayRow($"[{panel[index].Label}]", null));
                index++;
            }

            int end = index;
            while (end < panel.Length && !panel[end].IsHeading)
            {
                end++;
            }

            AddSection(rows, panel[index..end], column);
            index = end;
        }
    }

    private void AddSection(List<OverlayRow> rows, ReadOnlySpan<DebugPanelRow> panel, int column)
    {
        bool anyCommand = false;
        foreach (DebugPanelRow row in panel)
        {
            if (row.Kind == DebugPanelRowKind.Field)
            {
                rows.Add(new OverlayRow(row.Label.PadRight(column) + row.Value, null));
            }
            else
            {
                anyCommand = true;
            }
        }

        if (!anyCommand)
        {
            if (panel.IsEmpty)
            {
                rows.Add(new OverlayRow(EmptySection, null));
            }

            return;
        }

        rows.Add(new OverlayRow(CommandsHeading, null));
        foreach (DebugPanelRow row in panel)
        {
            if (row.Kind == DebugPanelRowKind.Field)
            {
                continue;
            }

            // A command or toggle from a hook belongs to the tick it forces. It runs inside that step,
            // ahead of the scene's own work, so its sounds and transitions are consumed with the step.
            // What it throws is the developer's and is not caught.
            Action activate = row.Activate!;
            string label = row.Kind == DebugPanelRowKind.Command ? row.Label : (row.On ? "[x] " : "[ ] ") + row.Label;
            rows.Add(new OverlayRow(Indent + label, () => _tick(activate)));
        }
    }
}
