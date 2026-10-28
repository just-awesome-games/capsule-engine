using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools.Overlay;

// Builds rows from DebugPanel hooks: the held scene's page and an entity's panel.
internal sealed class PanelRows
{
    private const string EmptySection = "<Nothing to show>";

    // Sets a section's commands and toggles under its heading, and an entity under its parent.
    private const string Indent = "  ";

    private readonly SceneHost _scenes;
    private readonly Action<HostAct, Action> _tick;
    private readonly Action<Entity> _open;
    private readonly Action _openCamera;
    private readonly DebugPanel _panel = new();

    // tick steps one tick with an act inside it, which a recording holds as the host act. open pushes an
    // entity's panel and openCamera the scene camera's, and neither steps.
    internal PanelRows(SceneHost scenes, Action<HostAct, Action> tick, Action<Entity> open, Action openCamera)
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
        AddRows(rows, PanelSubject.Scene, null);
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

            string label = entity.Name;
            rows.Add(new OverlayRow(label.PadLeft(label.Length + (depth * Indent.Length)), () => _open(entity)));
        }

        return _scenes.SceneKey;
    }

    // A field naming an entity, such as Parent, opens that entity's panel and steps nothing, as does a
    // child's row. The children list under Parent, each with a count of its own.
    internal string EntityPanel(Entity entity, List<OverlayRow> rows)
    {
        _panel.Clear();
        entity.RunDebugPanel(_panel);
        AddRows(rows, PanelSubject.Entity, entity);

        ReadOnlySpan<Entity> children = entity.Children;
        if (!children.IsEmpty)
        {
            // The entity writes its heading, then Parent when it has one.
            int at = entity.Parent is null ? 1 : 2;
            rows.Insert(at++, new OverlayRow("Children", null));
            foreach (Entity child in children)
            {
                rows.Insert(at++, new OverlayRow(Indent + child.Name, () => _open(child), ChildCount(child)));
            }
        }

        return entity.Name;
    }

    // The held scene's camera, titled by its type.
    internal string CameraPanel(List<OverlayRow> rows)
    {
        Camera camera = _scenes.Scene.Camera;
        _panel.Clear();
        camera.RunDebugPanel(_panel);
        AddRows(rows, PanelSubject.Camera, null);

        return camera.GetType().Name;
    }

    // Null for a leaf, which leaves its value column empty.
    private static string? ChildCount(Entity entity) =>
        entity.Children.Length switch
        {
            0 => null,
            1 => "1 child",
            int count => string.Create(CultureInfo.InvariantCulture, $"{count} children"),
        };

    // Fields, then commands and toggles under a Commands sub-heading. Only those and a field naming an
    // entity in the held scene are focusable.
    private void AddRows(List<OverlayRow> rows, PanelSubject subject, Entity? owner)
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

            AddSection(rows, panel, index, end, subject, owner);
            index = end;
        }
    }

    // The section is panel[start..end]. A command or toggle is recorded by its label and how many
    // same-labelled ones the whole panel wrote before it.
    private void AddSection(List<OverlayRow> rows, ReadOnlySpan<DebugPanelRow> panel, int start, int end, PanelSubject subject, Entity? owner)
    {
        ReadOnlySpan<DebugPanelRow> section = panel[start..end];
        bool anyCommand = false;
        foreach (DebugPanelRow row in section)
        {
            if (row is { Kind: DebugPanelRowKind.Field, Subject: Entity entity } && Holds(entity))
            {
                rows.Add(new OverlayRow(row.Label, () => _open(entity), entity.Name));
            }
            else if (row.Kind == DebugPanelRowKind.Field)
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
        for (int index = start; index < end; index++)
        {
            DebugPanelRow row = panel[index];
            if (row.Kind == DebugPanelRowKind.Field)
            {
                continue;
            }

            int occurrence = 0;
            for (int before = 0; before < index; before++)
            {
                if (panel[before].Kind is DebugPanelRowKind.Command or DebugPanelRowKind.Toggle && panel[before].Label == row.Label)
                {
                    occurrence++;
                }
            }

            // Runs inside the tick it forces. What it throws is the developer's and is not caught. The act
            // names the entity by where it stands when the row is chosen.
            Action activate = row.Activate!;
            string rowLabel = row.Label;
            string label = row.Kind == DebugPanelRowKind.Command ? row.Label : OverlayRow.Marked(row.On, row.Label);
            rows.Add(new OverlayRow(Indent + label, () => _tick(HostAct.Command(subject, owner, rowLabel, occurrence), activate)));
        }
    }
}
