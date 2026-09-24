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
/// <param name="Shape">
/// The convex polygon a tile of this type collides as, in world units from the tile's top-left corner with
/// Y down, or null for the whole tile. A grid rejects a shape on a tile with no layer, a rounded one, and
/// one reaching outside the tile.
/// </param>
/// <param name="OneWay">
/// Whether a tile of this type lets a mover pass from below and blocks it from above, and from the sides too
/// with SolidSides. A grid rejects it on a tile with no layer.
/// </param>
/// <param name="SolidSides">
/// Whether a one-way tile of this type also blocks from the sides, passing a mover only from below. A grid
/// rejects it on a tile that is not one-way.
/// </param>
public readonly record struct TileDefinition(
    string Type,
    int? Cell,
    string? Layer = null,
    Shape2D? Shape = null,
    bool OneWay = false,
    bool SolidSides = false);
