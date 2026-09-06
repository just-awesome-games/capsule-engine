using Capsule.Scenes.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>One engine-native tile-map entry. Its grid is anchored at the world origin.</summary>
/// <param name="Id">The entry's identity in the document's one id space.</param>
/// <param name="Grid">The tile grid carried by the entry's properties.</param>
/// <param name="ZIndex">
/// The authored draw band, or null where the placement authors none. A value — 0 included —
/// overwrites the composed <see cref="Tiles.TileMap"/>'s <see cref="Entity.ZIndex"/>; null leaves
/// the band the class gave itself.
/// </param>
public readonly record struct TileMapPlacement(int Id, TileGrid Grid, int? ZIndex = null);
