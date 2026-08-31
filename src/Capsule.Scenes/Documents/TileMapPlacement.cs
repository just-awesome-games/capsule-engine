using Capsule.Scenes.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>
/// One engine-native tile-map entry. Its grid is anchored at the world origin.
/// </summary>
/// <param name="Id">The entry's identity in the document's one id space.</param>
/// <param name="Grid">The tile grid carried by the entry's properties.</param>
public readonly record struct TileMapPlacement(int Id, TileGrid Grid);
