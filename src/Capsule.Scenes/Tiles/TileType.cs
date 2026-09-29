using Capsule.Physics;

namespace Capsule.Tiles;

/// <summary>
/// One entry of a grid's tile palette: its name, which cell of the grid's texture it draws, and how it
/// collides. A game subclasses it to give a tile type data of its own.
/// </summary>
/// <remarks>
/// One instance is shared by every cell painted with it, and every map built from the same grid shares it
/// too. State that belongs to one cell lives on an entity. A tile type with no cell draws nothing, and one
/// does not collide until it names a layer.
/// </remarks>
public class TileType
{
    /// <summary>The tile type's name, unique within the palette. It identifies the tile and is not a layer name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The cell of the grid's texture this tile type draws, counted from cell 0 left to right then top to
    /// bottom, or null to draw nothing. The grid's <c>Columns</c> and tile size turn it into a source region.
    /// </summary>
    public int? Cell { get; init; }

    /// <summary>The collision layer this tile type is on. Null means it does not collide.</summary>
    public string? Layer { get; init; }

    /// <summary>
    /// The convex polygon this tile type collides as, in world units from the tile's top-left corner with
    /// Y down, or null for the whole tile. A grid rejects a shape on a tile with no layer, a rounded one, and
    /// one reaching outside the tile.
    /// </summary>
    public Shape2D? Shape { get; init; }

    /// <summary>
    /// Whether this tile type lets a mover pass from below and blocks it from above, and from the sides too
    /// with <see cref="SolidSides"/>. A grid rejects it on a tile with no layer.
    /// </summary>
    public bool OneWay { get; init; }

    /// <summary>
    /// Whether a one-way tile type also blocks from the sides, passing a mover only from below. A grid
    /// rejects it on a tile that is not one-way.
    /// </summary>
    public bool SolidSides { get; init; }
}
