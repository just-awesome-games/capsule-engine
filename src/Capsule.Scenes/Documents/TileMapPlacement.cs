using Capsule.Scenes.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>
/// A scene document's terrain entry: its id in the document's one id space, and the grid its
/// properties carry. Terrain is anchored at the world origin, so the entry has no position of its
/// own.
/// </summary>
public readonly record struct TileMapPlacement(int Id, TileGrid Grid);
