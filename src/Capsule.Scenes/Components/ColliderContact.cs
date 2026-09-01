using System.Numerics;
using Capsule.Collision;

namespace Capsule.Scenes.Components;

/// <summary>One grid cell reached by a collider contact.</summary>
/// <param name="Grid">The collision grid that owns the cell.</param>
/// <param name="X">The cell's column.</param>
/// <param name="Y">The cell's row.</param>
/// <param name="Owner">The object supplied when the grid was registered, if any.</param>
public readonly record struct GridCellContact(GridCollider Grid, int X, int Y, object? Owner);

/// <summary>Something a <see cref="Collider"/> is touching, named the way the game authored it.</summary>
public readonly struct ColliderContact
{
    internal ColliderContact(
        CollisionTarget target,
        string tag,
        Vector2 point,
        Vector2 normal,
        Collider? otherCollider,
        GridCellContact? cell)
    {
        Target = target;
        Tag = tag;
        Point = point;
        Normal = normal;
        OtherCollider = otherCollider;
        Cell = cell;
    }

    /// <summary>The touched thing's tag as a name.</summary>
    public string Tag { get; }

    /// <summary>A world-space point on the touched surface.</summary>
    public Vector2 Point { get; }

    /// <summary>
    /// The unit surface normal pointing from what was touched back towards this collider. In a
    /// Y-down world, standing on something gives (0, -1).
    /// </summary>
    public Vector2 Normal { get; }

    /// <summary>The other collider, or null when <see cref="Cell"/> names a grid cell.</summary>
    public Collider? OtherCollider { get; }

    /// <summary>The grid cell touched, or null when <see cref="OtherCollider"/> names another collider.</summary>
    public GridCellContact? Cell { get; }

    /// <summary>The entity reached through <see cref="OtherCollider"/> or the grid's owner.</summary>
    public Entity? OtherEntity => OtherCollider?.Entity ?? Cell?.Owner as Entity;

    // Stable low-level identity used to pair enter and exit without exposing grid implementation
    // details through the scene-level API.
    internal CollisionTarget Target { get; }
}
