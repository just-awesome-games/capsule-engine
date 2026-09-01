namespace Capsule.Collision;

/// <summary>
/// What a query found: either a collider, or one cell of a grid collider. A cell keeps its own
/// identity here — its coordinates and tag — so callers can reach authored content rather than
/// an anonymous piece of terrain.
/// </summary>
public readonly record struct CollisionTarget
{
    private CollisionTarget(ColliderHandle collider, bool isGridCell, int cellX, int cellY, CollisionTag tag)
    {
        Collider = collider;
        IsGridCell = isGridCell;
        CellX = cellX;
        CellY = cellY;
        Tag = tag;
    }

    /// <summary>The collider found; for a grid cell, the grid collider it belongs to.</summary>
    public ColliderHandle Collider { get; }

    /// <summary>Whether this is one cell of a grid collider rather than a shape collider.</summary>
    public bool IsGridCell { get; }

    /// <summary>The cell's column when <see cref="IsGridCell"/>; zero otherwise.</summary>
    public int CellX { get; }

    /// <summary>The cell's row when <see cref="IsGridCell"/>; zero otherwise.</summary>
    public int CellY { get; }

    /// <summary>The tag the collider or grid cell carries.</summary>
    public CollisionTag Tag { get; }

    /// <summary>A target naming one collider.</summary>
    public static CollisionTarget ForCollider(ColliderHandle collider, CollisionTag tag) =>
        new(collider, false, 0, 0, tag);

    /// <summary>A target naming one cell of a grid collider.</summary>
    public static CollisionTarget ForGridCell(ColliderHandle grid, int x, int y, CollisionTag tag) =>
        new(grid, true, x, y, tag);
}
