namespace Capsule.Tiles;

/// <summary>One frame of an animated <see cref="TileType"/>: a cell of the grid's texture and how many fixed steps it is held.</summary>
/// <param name="Cell">The cell of the grid's texture this frame draws, counted as <see cref="TileType.Cell"/> is.</param>
/// <param name="Ticks">How many fixed steps the frame is held, at least 1.</param>
public readonly record struct TileFrame(int Cell, int Ticks);
