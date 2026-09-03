using Capsule.Collision;

namespace Capsule.Scenes.Tiles;

/// <summary>
/// One entry of a grid's tile palette: its semantic type, which cell of the grid's texture it
/// draws, and what it collides as. A tile type with no cell is semantic only — it draws nothing
/// without weakening tile identity — and one collides as nothing until it names a layer.
/// </summary>
/// <param name="Type">The tile type's name, unique within the palette. Identity, never a layer.</param>
/// <param name="Cell">
/// The cell of the grid's texture a tile of this type draws, counted left to right then top to
/// bottom from cell 0, or null to draw nothing. A grid's <c>Columns</c> and tile size turn it into
/// a source region.
/// </param>
/// <param name="Layer">The collision layer a tile of this type is on; null means it collides as nothing.</param>
/// <param name="CollidableFaces">Which sides of the tile collide; every side by default.</param>
public readonly record struct TileDefinition(
    string Type,
    int? Cell,
    string? Layer = null,
    CellFaces2D CollidableFaces = CellFaces2D.All);
