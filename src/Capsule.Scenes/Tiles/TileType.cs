using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Tiles;

/// <summary>
/// One entry of a grid's tile palette: its name, which cell of the grid's texture it draws, and how it
/// collides. A game subclasses it to give a tile type data of its own.
/// </summary>
/// <remarks>
/// One instance is shared by every cell painted with it, and every map built from the same grid shares it
/// too. A scene document's palette is read once, and every scene composed from that document holds the same
/// instances. A tile type therefore keeps no state that changes while a scene runs: a subclass declares each
/// authorable member with an <c>init</c> accessor, which the build checks, and state that belongs to one cell
/// lives on an entity. A tile type with no cell, frames or rules draws nothing, and one does not collide until it
/// names a layer.
/// </remarks>
public class TileType
{
    /// <summary>The tile type's name, unique within the palette. It identifies the tile and is not a layer name.</summary>
    [Authorable(Required = true)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The cell of the grid's texture this tile type draws, counted from cell 0 left to right then top to
    /// bottom, or null for a tile type that animates through <see cref="Frames"/>, picks a look by
    /// <see cref="Rules"/> or draws nothing. The grid's <c>Columns</c> and tile size turn it into a source region.
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

    /// <summary>
    /// The looks this tile type picks from by its neighbours, in place of <see cref="Cell"/> or <see cref="Frames"/>,
    /// or null for a tile type that draws one look.
    /// </summary>
    /// <remarks>
    /// Each cell draws the rule whose <see cref="TileRule.Joined"/> names exactly the sides whose neighbour's
    /// <see cref="Terrain"/> is <see cref="Joins"/>. Only the sides some rule names are read, a corner joins only
    /// where both its sides do, and a neighbour past the map's edge joins. A grid rejects rules that leave a
    /// combination of those sides without a look. Several rules for one combination are picked between by the
    /// cell's position, the same way every run. The map picks a cell's look when it is painted and again whenever
    /// a neighbour's terrain changes, and collision and <c>TileAt</c> follow the tile type and never the look.
    /// </remarks>
    /// <example>
    /// <code>
    /// TileType ledge = new()
    /// {
    ///     Name = "ledge",
    ///     Terrain = "ledge",
    ///     Rules = [new(8), new(9, TileSides.Right), new(10, TileSides.Left | TileSides.Right), new(11, TileSides.Left)],
    /// };
    /// </code>
    /// </example>
    [Authorable]
    public TileRule[]? Rules { get; init; }

    /// <summary>
    /// What a cell of this tile type counts as to its neighbours' <see cref="Rules"/>, or null for nothing they join.
    /// </summary>
    [Authorable]
    public string? Terrain { get; init; }

    /// <summary>The terrain this tile type's <see cref="Rules"/> join, or null for its own <see cref="Terrain"/>.</summary>
    [Authorable]
    public string? Joins { get; init; }

    /// <summary>The collision layer this tile type is on. Null means it does not collide.</summary>
    [Authorable]
    public string? Layer { get; init; }

    /// <summary>
    /// The convex polygon this tile type collides as, three to <see cref="Physics.Shape2D.MaxPoints"/> points in
    /// world units from the tile's top-left corner with Y down, or null for the whole tile. A grid rejects a shape on a tile with no
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

    /// <summary>
    /// The velocity, in world units per second, at which each cell of this tile type carries the bodies
    /// riding it without moving, where zero, the default, carries nothing.
    /// </summary>
    /// <remarks>
    /// A grid rejects it on a tile with no layer. A cell's flip or quarter turn turns the velocity as it
    /// turns the cell's shape.
    /// <see cref="Physics.KinematicBody2D.MovedBy"/> says which bodies ride it and when they are carried.
    /// </remarks>
    [Authorable]
    public Vector2 SurfaceVelocity { get; init; }
}
