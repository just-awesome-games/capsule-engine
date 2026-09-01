namespace Capsule.Scenes.Tiles;

/// <summary>The collision shape a tile type contributes to its tile map.</summary>
public enum TileCollision
{
    /// <summary>Nothing; the cell is empty as far as collision is concerned.</summary>
    None,

    /// <summary>The whole cell, as a box.</summary>
    Solid,

    /// <summary>
    /// The cell's low edge on Y alone, blocking only what crosses it travelling towards +Y in a
    /// Y-down world.
    /// </summary>
    OneWay,
}
