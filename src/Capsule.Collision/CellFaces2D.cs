namespace Capsule.Collision;

/// <summary>
/// Which sides of a grid cell collide. Named by grid direction in a Y-down world:
/// <see cref="Top"/> is the cell's -Y side and <see cref="Bottom"/> its +Y side.
/// <para>
/// <see cref="All"/> is a solid cell — the whole box, with faces shared with a solid neighbour
/// culled so a flat run is one surface. Any smaller set is that many one-directional edges: a face
/// blocks only what crosses it travelling into the cell, never motion along it and never something
/// that started on the far side of it.
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

    /// <summary>The cell's -Y side; the one a falling body lands on in a Y-down world.</summary>
    Top = 4,

    /// <summary>The cell's +Y side.</summary>
    Bottom = 8,

    /// <summary>Every side: the whole cell as a box.</summary>
    All = Left | Right | Top | Bottom,
}
