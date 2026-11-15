using System.Numerics;
using Capsule.Scenes;
using Capsule.Tiles;

namespace Capsule.Physics;

/// <summary>One tile-map cell a collider contact or a query reached.</summary>
/// <param name="Map">The tile map that holds the cell.</param>
/// <param name="X">The cell's column.</param>
/// <param name="Y">The cell's row.</param>
public readonly record struct TileContact2D(TileMap Map, int X, int Y)
{
    /// <summary>The palette entry the cell holds now, the same instance <see cref="TileMap.TileAt(int, int)"/> returns.</summary>
    public TileType Type => Map.TileAt(X, Y);

    /// <summary>How the tile the cell holds now is mirrored or turned.</summary>
    public TileTransform Transform => Map.TransformAt(X, Y);

    // The cell target names when owner, its grid's user data, is a tile map's collider.
    internal static TileContact2D? Of(object? owner, in CollisionTarget target) =>
        target.IsGridCell && owner is TileMapCollider2D { Entity: TileMap map }
            ? new TileContact2D(map, target.CellX, target.CellY)
            : null;
}

/// <summary>Something a <see cref="Collider2D"/> is touching, described in the game's own terms.</summary>
public readonly struct ColliderContact2D
{
    private readonly CollisionWorld2D? _world;

    // The other collider, the tile map whose cell was touched, or null. Holding it keeps both named
    // after the other side leaves the world, and the rest derives from it and the contact.
    private readonly object? _other;
    private readonly Contact2D _contact;

    private ColliderContact2D(CollisionWorld2D? world, object? other, in Contact2D contact)
    {
        _world = world;
        _other = other;
        _contact = contact;
    }

    /// <summary>A world-space point on the touched surface.</summary>
    public Vector2 Point => _contact.Point;

    /// <summary>
    /// The unit surface normal pointing from the touched surface back towards this collider. In a Y-down
    /// world, standing on something reads (0, -1).
    /// </summary>
    public Vector2 Normal => _contact.Normal;

    /// <summary>
    /// How far the two shapes overlap along <see cref="Normal"/>, in world units. <c>Normal * Depth</c>
    /// leads out of the touched thing.
    /// </summary>
    /// <remarks>Zero when the shapes merely touch, and on every contact a sweep reports.</remarks>
    public float Depth => _contact.Depth;

    /// <summary>The other collider, or null when a grid cell was touched.</summary>
    public Collider2D? OtherCollider => _other as Collider2D;

    /// <summary>The tile-map cell touched, or null when the contact is not a tile map's cell.</summary>
    public TileContact2D? Tile => _other is TileMap map ? new TileContact2D(map, Target.CellX, Target.CellY) : null;

    // A stable identity that pairs an enter with its exit without exposing grid internals through the
    // scene-level API.
    internal CollisionTarget Target => _contact.Target;

    /// <summary>The collision layer the touched thing is on.</summary>
    public CollisionLayer Layer => Target.Layer;

    /// <summary>The entity behind <see cref="OtherCollider"/>, or the tile map when a tile was touched.</summary>
    /// <remarks>
    /// It is null on a default contact. It is also null once the other collider has been detached from
    /// its entity, which a <see cref="Collider2D.ContactExited"/> handler sees when the other side left
    /// that way.
    /// </remarks>
    public Entity? OtherEntity => _other is TileMap map ? map : OtherCollider?.Entity;

    /// <summary>
    /// The touched surface's layer name, for a log line. Reads empty on a default contact.
    /// </summary>
    /// <remarks>A handler deciding what to do compares <see cref="Layer"/>, which costs no lookup.</remarks>
    public string LayerName => _world?.NameOf(Layer) ?? string.Empty;

    // Describes a contact a query of world just found, naming the collider behind it or, for a tile
    // map's cell, the map.
    internal static ColliderContact2D Of(CollisionWorld2D world, in Contact2D contact)
    {
        object? owner = world.UserDataOf(contact.Target.Collider);
        object? other = contact.Target.IsGridCell ? TileContact2D.Of(owner, contact.Target)?.Map : owner as Collider2D;

        return new ColliderContact2D(world, other, contact);
    }

    // The same target found again a step later. It skips the lookup Of makes, because a target keeps
    // what it names while it lives.
    internal ColliderContact2D MovedTo(in Contact2D contact) => new(_world, _other, contact);
}
