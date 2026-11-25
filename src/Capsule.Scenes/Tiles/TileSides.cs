namespace Capsule.Tiles;

/// <summary>The sides and corners of a cell, which a <see cref="TileRule"/> names to say which neighbours join it.</summary>
/// <remarks>A document authors several as their names joined by commas, <c>"up, right"</c>.</remarks>
[Flags]
public enum TileSides : byte
{
    /// <summary>No side.</summary>
    None = 0,

    /// <summary>The cell above.</summary>
    Up = 1,

    /// <summary>The cell to the right.</summary>
    Right = 2,

    /// <summary>The cell below.</summary>
    Down = 4,

    /// <summary>The cell to the left.</summary>
    Left = 8,

    /// <summary>The cell above and to the right, which joins only where <see cref="Up"/> and <see cref="Right"/> both do.</summary>
    UpRight = 16,

    /// <summary>The cell below and to the right, which joins only where <see cref="Down"/> and <see cref="Right"/> both do.</summary>
    DownRight = 32,

    /// <summary>The cell below and to the left, which joins only where <see cref="Down"/> and <see cref="Left"/> both do.</summary>
    DownLeft = 64,

    /// <summary>The cell above and to the left, which joins only where <see cref="Up"/> and <see cref="Left"/> both do.</summary>
    UpLeft = 128,
}
