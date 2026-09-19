using Capsule.Physics;

namespace Capsule.Tiles;

/// <summary>
/// One entry of a grid's tile palette, holding its semantic type, which cell of the grid's texture it draws,
/// and how it collides. A tile type with no cell draws nothing, and a tile type does not collide until it
/// names a layer.
/// </summary>
/// <param name="Type">The tile type's name, unique within the palette. It identifies the tile and is not a layer name.</param>
/// <param name="Cell">
/// The cell of the grid's texture a tile of this type draws, counted from cell 0 left to right then top to
/// bottom, or null to draw nothing. The grid's <c>Columns</c> and tile size turn it into a source region.
/// </param>
/// <param name="Layer">The collision layer a tile of this type is on. Null means it does not collide.</param>
/// <param name="CollidableFaces">Which sides of the tile collide. Every side by default.</param>
public readonly record struct TileDefinition(
    string Type,
    int? Cell,
    string? Layer = null,
    CellFaces2D CollidableFaces = CellFaces2D.All);
