using System.Numerics;
using Capsule.Scenes;
using Capsule.Tiles;

namespace Capsule.Physics;

/// <summary>One tile-map cell reached by a collider contact.</summary>
/// <param name="Map">The tile map that holds the cell.</param>
/// <param name="X">The cell's column.</param>
/// <param name="Y">The cell's row.</param>
public readonly record struct TileContact2D(TileMap Map, int X, int Y)
{
    /// <summary>The tile type name the cell holds now.</summary>
    public string Type => Map.TileAt(X, Y);

    /// <summary>How the tile the cell holds now is mirrored or turned.</summary>
    public TileTransform Transform => Map.TransformAt(X, Y);
}

/// <summary>Something a <see cref="Collider2D"/> is touching, described in the game's own terms.</summary>
public readonly struct ColliderContact2D
{
    private readonly CollisionWorld2D? _world;

    internal ColliderContact2D(
        CollisionWorld2D world,
        CollisionTarget target,
        Vector2 point,
        Vector2 normal,
        float depth,
        Collider2D? otherCollider,
        TileContact2D? tile)
    {
        _world = world;
        Target = target;
        Point = point;
        Normal = normal;
        Depth = depth;
        OtherCollider = otherCollider;
        Tile = tile;
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

    /// <summary>
    /// How far the two shapes overlap along <see cref="Normal"/>, in world units. <c>Normal * Depth</c>
    /// leads out of the touched thing.
    /// </summary>
    /// <remarks>Zero when the shapes merely touch, and on every contact a sweep reports.</remarks>
    public float Depth { get; }

    /// <summary>The other collider, or null when a grid cell was touched.</summary>
    public Collider2D? OtherCollider { get; }

    /// <summary>The tile-map cell touched, or null when the contact is not a tile map's cell.</summary>
    public TileContact2D? Tile { get; }

    /// <summary>The entity behind <see cref="OtherCollider"/>, or the tile map when a tile was touched.</summary>
    public Entity? OtherEntity => OtherCollider?.Entity ?? Tile?.Map;

    /// <summary>
    /// The touched surface's layer name, for a log line. Reads empty on a default contact.
    /// </summary>
    /// <remarks>A handler deciding what to do compares <see cref="Layer"/>, which costs no lookup.</remarks>
    public string LayerName => _world?.NameOf(Layer) ?? string.Empty;

    // A stable identity that pairs an enter with its exit without exposing grid internals through the
    // scene-level API.
    internal CollisionTarget Target { get; }
}
