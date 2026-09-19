namespace Capsule.Physics;

/// <summary>
/// Which sides of a grid cell collide. Names follow grid direction in a Y-down world, where
/// <see cref="Top"/> is the -Y side and <see cref="Bottom"/> the +Y side.
/// <para>
/// <see cref="All"/> is a solid cell. Faces shared with a solid neighbour are culled, leaving a flat
/// run to read as one surface. A smaller set gives one-directional edges. A face blocks motion crossing
/// it into the cell, and ignores motion along the face or a body that started on the far side.
/// </para>
/// </summary>
[Flags]
public enum CellFaces2D
{
    /// <summary>No side collides.</summary>
    None = 0,

    /// <summary>The cell's -X side.</summary>
    Left = 1,

    /// <summary>The cell's +X side.</summary>
    Right = 2,

    /// <summary>The cell's -Y side. A falling body lands on it in a Y-down world.</summary>
    Top = 4,

    /// <summary>The cell's +Y side.</summary>
    Bottom = 8,

    /// <summary>Every side, the cell as a solid box.</summary>
    All = Left | Right | Top | Bottom,
}
