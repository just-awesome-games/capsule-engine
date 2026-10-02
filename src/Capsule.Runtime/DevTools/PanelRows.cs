using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools;

// Builds rows from DebugPanel hooks: the held scene's page and an entity's panel.
internal sealed class PanelRows
{
    private const string EmptySection = "<Nothing to show>";

    // Sets a section's commands and toggles under its heading, and an entity under its parent.
    private const string Indent = "  ";

    private readonly SceneHost _scenes;
    private readonly Action<Action> _tick;
    private readonly Action<Entity> _open;
    private readonly Action _openCamera;
    private readonly DebugPanel _panel = new();

    // tick steps one tick with an act inside it. open pushes an entity's panel and openCamera the
    // scene camera's, and neither steps.
    internal PanelRows(SceneHost scenes, Action<Action> tick, Action<Entity> open, Action openCamera)
    {
        _scenes = scenes;
        _tick = tick;
        _open = open;
        _openCamera = openCamera;
    }

    internal bool Holds(Entity entity) => ReferenceEquals(entity.SceneOrNull, _scenes.Scene);

    // Entity rows are indented by depth to read as a tree. The Camera row opens the camera's panel.
    // Returns the page's title.
    internal string ScenePage(List<OverlayRow> rows)
    {
        Scene scene = _scenes.Scene;
        _panel.Clear();
        scene.RunDebugPanel(_panel);
        AddRows(rows);
        rows.Insert(1, new OverlayRow("Camera", _openCamera, scene.Camera.GetType().Name));

        rows.Add(new OverlayRow(string.Empty, null));
        rows.Add(new OverlayRow("[Entities]", null));

        ReadOnlySpan<Entity> entities = scene.Entities;
        if (entities.IsEmpty)
        {
            rows.Add(new OverlayRow(EmptySection, null));
        }

        foreach (Entity entity in entities)
        {
            int depth = 0;
            for (Entity? above = entity.Parent; above is not null; above = above.Parent)
            {
                depth++;
            }

            string label = DisplayName(entity);
            rows.Add(new OverlayRow(label.PadLeft(label.Length + (depth * Indent.Length)), () => _open(entity)));
        }

        return scene.GetType().Name;
    }

    // The Parent row opens the parent's panel and steps nothing.
    internal string EntityPanel(Entity entity, List<OverlayRow> rows)
    {
        _panel.Clear();
        entity.RunDebugPanel(_panel);
        AddRows(rows);

        if (entity.Parent is { } parent)
        {
            rows.Insert(1, new OverlayRow("Parent", () => _open(parent), DisplayName(parent)));
        }

        return DisplayName(entity);
    }

    // The held scene's camera, titled by its type.
    internal string CameraPanel(List<OverlayRow> rows)
    {
        Camera camera = _scenes.Scene.Camera;
        _panel.Clear();
        camera.RunDebugPanel(_panel);
        AddRows(rows);

        return camera.GetType().Name;
    }

    // Name or type name, numbered among same-named siblings: Enemy, Enemy (1), Enemy (2).
    internal static string DisplayName(Entity entity)
    {
        string name = entity.Name ?? entity.GetType().Name;
        int before = 0;
        ReadOnlySpan<Entity> peers = entity.Parent is { } parent ? parent.Children : entity.Scene.Entities;
        foreach (Entity peer in peers)
        {
            if (ReferenceEquals(peer, entity))
            {
                break;
            }

            if (ReferenceEquals(peer.Parent, entity.Parent) && (peer.Name ?? peer.GetType().Name) == name)
            {
                before++;
            }
        }

        return before == 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{name} ({before})");
    }

    // Fields, then commands and toggles under a Commands sub-heading. Only those are focusable.
    private void AddRows(List<OverlayRow> rows)
    {
        ReadOnlySpan<DebugPanelRow> panel = _panel.Rows;
        int index = 0;
        while (index < panel.Length)
        {
            if (index > 0)
            {
                rows.Add(new OverlayRow(string.Empty, null));
            }

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

            AddSection(rows, panel[index..end]);
            index = end;
        }
    }

    private void AddSection(List<OverlayRow> rows, ReadOnlySpan<DebugPanelRow> section)
    {
        bool anyCommand = false;
        foreach (DebugPanelRow row in section)
        {
            if (row.Kind == DebugPanelRowKind.Field)
            {
                rows.Add(new OverlayRow(row.Label, null, row.Value));
            }
            else
            {
                anyCommand = true;
            }
        }

        if (!anyCommand)
        {
            if (section.IsEmpty)
            {
                rows.Add(new OverlayRow(EmptySection, null));
            }

            return;
        }

        rows.Add(new OverlayRow(Indent + "(Commands)", null));
        foreach (DebugPanelRow row in section)
        {
            if (row.Kind == DebugPanelRowKind.Field)
            {
                continue;
            }

            // Runs inside the tick it forces. What it throws is the developer's and is not caught.
            Action activate = row.Activate!;
            string label = row.Kind == DebugPanelRowKind.Command ? row.Label : OverlayRow.Marked(row.On, row.Label);
            rows.Add(new OverlayRow(Indent + label, () => _tick(activate)));
        }
    }
}
