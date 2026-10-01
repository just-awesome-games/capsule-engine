using System.Numerics;
using Capsule.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>One engine-native tile-map entry. Its grid is anchored at the world origin.</summary>
/// <param name="Id">The entry's id in the document's single id space.</param>
/// <param name="Grid">The tile grid the entry's properties carry.</param>
/// <param name="ZIndex">
/// The authored draw band, or null when the placement authors none. Any value, including 0, overwrites the
/// composed <see cref="Tiles.TileMap"/>'s <see cref="Entity.ZIndex"/>, and null keeps the band the class set
/// for itself.
/// </param>
/// <param name="ScrollFactor">
/// The authored scroll factor, or null when the placement authors none. A value overwrites the composed map's
/// <see cref="Entity.ScrollFactor"/>, which a map with a collider rejects.
/// </param>
/// <param name="HasCollider">
/// Whether the composed map gets a <see cref="TileMapCollider2D"/>, false by default. The grid's palette must
/// then name a layer.
/// </param>
public readonly record struct TileMapPlacement(
    int Id,
    TileGrid Grid,
    int? ZIndex = null,
    Vector2? ScrollFactor = null,
    bool HasCollider = false);
