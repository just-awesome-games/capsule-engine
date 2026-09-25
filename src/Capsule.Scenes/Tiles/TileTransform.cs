namespace Capsule.Tiles;

/// <summary>How one tile is mirrored or turned inside its own cell. The tile draws and collides the same way.</summary>
/// <remarks>
/// The bits apply in a fixed order. <see cref="Transpose"/> swaps a point's x and y first, then
/// <see cref="FlipX"/> mirrors it left to right, then <see cref="FlipY"/> mirrors it top to bottom.
/// Every combination of the three is valid. A one-way tile passes bodies from below whichever way it
/// faces, and blocks from above.
/// </remarks>
[Flags]
public enum TileTransform : byte
{
    /// <summary>The tile as its palette entry draws it.</summary>
    None = 0,

    /// <summary>Mirrors the tile left to right.</summary>
    FlipX = 1,

    /// <summary>Mirrors the tile top to bottom.</summary>
    FlipY = 2,

    /// <summary>
    /// Swaps the tile's x and y, which mirrors it across the diagonal from its top-left corner. It
    /// applies before either flip.
    /// </summary>
    Transpose = 4,

    /// <summary>
    /// Turns the tile a quarter turn clockwise. A point at (x, y) in a tile of edge T lands at (T - y, x).
    /// </summary>
    Rotate90 = Transpose | FlipX,

    /// <summary>Turns the tile half a turn.</summary>
    Rotate180 = FlipX | FlipY,

    /// <summary>
    /// Turns the tile a quarter turn counter-clockwise. A point at (x, y) in a tile of edge T lands at (y, T - x).
    /// </summary>
    Rotate270 = Transpose | FlipY,
}
