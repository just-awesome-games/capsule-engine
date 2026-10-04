using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Tiles;

/// <summary>
/// One entry of a grid's tile palette: its name, which cell of the grid's texture it draws, and how it
/// collides. A game subclasses it to give a tile type data of its own.
/// </summary>
/// <remarks>
/// One instance is shared by every cell painted with it, and every map built from the same grid shares it
/// too. State that belongs to one cell lives on an entity. A tile type with no cell and no frames draws
/// nothing, and one does not collide until it names a layer.
/// </remarks>
public class TileType
{
    /// <summary>The tile type's name, unique within the palette. It identifies the tile and is not a layer name.</summary>
    [Authorable(Required = true)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The cell of the grid's texture this tile type draws, counted from cell 0 left to right then top to
    /// bottom, or null for a tile type that animates through <see cref="Frames"/> or draws nothing. The
    /// grid's <c>Columns</c> and tile size turn it into a source region.
    /// </summary>
    [Authorable]
    public int? Cell { get; init; }

    /// <summary>
    /// The cells this tile type draws in turn, looping, in place of <see cref="Cell"/>, or null for a tile type
    /// that does not animate.
    /// </summary>
    /// <remarks>
    /// A grid rejects a tile type that sets both <see cref="Cell"/> and frames. Frames play from the map's
    /// own steps. A pause, a freeze or the map's <c>StepMode</c> holds them. A map starts at the first frame,
    /// and every cell of this tile type on one map shows the same frame, including a cell painted with it
    /// later. Collision and <c>TileAt</c> follow the tile type and never the frame.
    /// </remarks>
    /// <example>
    /// <code>
    /// TileType falls = new() { Name = "falls", Frames = [new(4, 8), new(5, 8), new(6, 8), new(7, 8)] };
    /// </code>
    /// </example>
    [Authorable]
    public TileFrame[]? Frames { get; init; }

    /// <summary>The collision layer this tile type is on. Null means it does not collide.</summary>
    [Authorable]
    public string? Layer { get; init; }

    /// <summary>
    /// The convex polygon this tile type collides as, three or four points in world units from the tile's
    /// top-left corner with Y down, or null for the whole tile. A grid rejects a shape on a tile with no
    /// layer, and one reaching outside the tile.
    /// </summary>
    /// <example>
    /// <code>
    /// TileType slope = new() { Name = "slope", Cell = 3, Layer = "solid", Shape = [new(0, 16), new(16, 0), new(16, 16)] };
    /// </code>
    /// </example>
    [Authorable]
    public Vector2[]? Shape { get; init; }

    /// <summary>
    /// Whether this tile type lets a mover pass from below and blocks it from above, and from the sides too
    /// with <see cref="SolidSides"/>. A grid rejects it on a tile with no layer.
    /// </summary>
    [Authorable]
    public bool OneWay { get; init; }

    /// <summary>
    /// Whether a one-way tile type also blocks from the sides, passing a mover only from below. A grid
    /// rejects it on a tile that is not one-way.
    /// </summary>
    [Authorable]
    public bool SolidSides { get; init; }
}
