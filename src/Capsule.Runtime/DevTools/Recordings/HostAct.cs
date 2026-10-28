using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Runtime.DevTools.Recordings;

internal enum HostActKind : byte
{
    Restart = 1,
    Load = 2,
    Move = 3,
    Command = 4,
    Exit = 5,
}

// Whose panel a command came from. An entity is named by its tree path.
internal enum PanelSubject : byte
{
    Scene = 0,
    Camera = 1,
    Entity = 2,
}

// An overlay act a recording holds and a replay repeats at the same tick. Name is a Load's registration
// or a Command's row label. Occurrence counts the same-labelled commands and toggles before it in its
// panel. Path is a Move's or an entity panel's entity: its root's index among the roots, then a child
// index per level.
internal readonly record struct HostAct(
    HostActKind Kind,
    string? Name = null,
    int[]? Path = null,
    Vector2 Point = default,
    PanelSubject Subject = PanelSubject.Scene,
    int Occurrence = 0)
{
    internal static HostAct Restart() => new(HostActKind.Restart);

    internal static HostAct Exit() => new(HostActKind.Exit);

    internal static HostAct Load(string registration) => new(HostActKind.Load, registration);

    internal static HostAct Move(Entity entity, Vector2 point) => new(HostActKind.Move, Path: EntityPath.Of(entity), Point: point);

    internal static HostAct Command(PanelSubject subject, Entity? entity, string label, int occurrence) =>
        new(HostActKind.Command, label, entity is null ? null : EntityPath.Of(entity), Subject: subject, Occurrence: occurrence);
}

// An entity's place in its scene's tree, which names it across runs.
internal static class EntityPath
{
    internal static int[] Of(Entity entity)
    {
        List<int> path = [];
        Entity current = entity;
        while (current.Parent is { } parent)
        {
            path.Add(SlotOf(parent.Children, current));
            current = parent;
        }

        int root = 0;
        foreach (Entity held in current.Scene.Entities)
        {
            if (ReferenceEquals(held, current))
            {
                break;
            }

            if (held.Parent is null)
            {
                root++;
            }
        }

        path.Add(root);
        path.Reverse();

        return [.. path];
    }

    // Null when the scene holds nothing at that path.
    internal static Entity? Find(Scene scene, int[] path)
    {
        if (path.Length == 0)
        {
            return null;
        }

        Entity? found = null;
        int root = 0;
        foreach (Entity held in scene.Entities)
        {
            if (held.Parent is null && root++ == path[0])
            {
                found = held;
                break;
            }
        }

        for (int level = 1; found is not null && level < path.Length; level++)
        {
            ReadOnlySpan<Entity> children = found.Children;
            found = (uint)path[level] < (uint)children.Length ? children[path[level]] : null;
        }

        return found is not null && ReferenceEquals(found.SceneOrNull, scene) ? found : null;
    }

    private static int SlotOf(ReadOnlySpan<Entity> entities, Entity entity)
    {
        for (int index = 0; index < entities.Length; index++)
        {
            if (ReferenceEquals(entities[index], entity))
            {
                return index;
            }
        }

        return -1;
    }
}
