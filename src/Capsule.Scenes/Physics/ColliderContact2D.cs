using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Physics;

/// <summary>One grid cell reached by a collider contact.</summary>
/// <param name="Grid">The collision grid that owns the cell.</param>
/// <param name="X">The cell's column.</param>
/// <param name="Y">The cell's row.</param>
/// <param name="Owner">The object supplied when the grid was registered, or null when none was.</param>
public readonly record struct GridCellContact2D(GridCollider2D Grid, int X, int Y, object? Owner);

/// <summary>Something a <see cref="Collider2D"/> is touching, described in the game's own terms.</summary>
public readonly struct ColliderContact2D
{
    private readonly CollisionWorld2D? _world;

    internal ColliderContact2D(
        CollisionWorld2D world,
        CollisionTarget target,
        Vector2 point,
        Vector2 normal,
        Collider2D? otherCollider,
        GridCellContact2D? cell)
    {
        _world = world;
        Target = target;
        Point = point;
        Normal = normal;
        OtherCollider = otherCollider;
        Cell = cell;
    }

    /// <summary>The collision layer the touched thing is on.</summary>
    public CollisionLayer Layer => Target.Layer;

    /// <summary>A world-space point on the touched surface.</summary>
    public Vector2 Point { get; }

    /// <summary>
    /// The unit surface normal pointing from the touched surface back towards this collider. In a Y-down
    /// world, standing on something reads (0, -1).
    /// </summary>
    public Vector2 Normal { get; }

    /// <summary>The other collider, or null when <see cref="Cell"/> names a grid cell.</summary>
    public Collider2D? OtherCollider { get; }

    /// <summary>The grid cell touched, or null when <see cref="OtherCollider"/> holds another collider.</summary>
    public GridCellContact2D? Cell { get; }

    /// <summary>The entity behind <see cref="OtherCollider"/>, or the grid's owner when a cell was touched.</summary>
    public Entity? OtherEntity => OtherCollider?.Entity ?? Cell?.Owner as Entity;

    /// <summary>
    /// The touched surface's layer as the name it was interned under, which is the readable form for a
    /// log line. Reads empty on a default contact, which has no world. A handler deciding what to do
    /// compares <see cref="Layer"/> instead, which costs no lookup.
    /// </summary>
    public string LayerName => _world?.NameOf(Layer) ?? string.Empty;

    // A stable identity that pairs an enter with its exit without exposing grid internals through the
    // scene-level API.
    internal CollisionTarget Target { get; }
}
