namespace Capsule.Collision;

/// <summary>The shape one profile contributes to its grid collider.</summary>
public enum CellCollision
{
    /// <summary>Nothing; the cell is empty as far as collision is concerned.</summary>
    None,

    /// <summary>The whole cell, as a box. Faces shared with another solid cell never contact.</summary>
    Solid,

    /// <summary>
    /// The cell's low edge on Y alone, blocking only what crosses it travelling towards +Y — in a
    /// Y-down world, landing on it from above. It never blocks movement along X, movement towards
    /// -Y, or anything that started past it.
    /// </summary>
    OneWay,
}
