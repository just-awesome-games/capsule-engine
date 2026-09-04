namespace Capsule.Collision;

/// <summary>
/// What a query found: either a collider, or one cell of a grid collider, identified by its
/// coordinates and its layer.
/// </summary>
public readonly record struct CollisionTarget
{
    private CollisionTarget(ColliderHandle collider, bool isGridCell, int cellX, int cellY, CollisionLayer layer)
    {
        Collider = collider;
        IsGridCell = isGridCell;
        CellX = cellX;
        CellY = cellY;
        Layer = layer;
    }

    /// <summary>The collider found; for a grid cell, the grid collider it belongs to.</summary>
    public ColliderHandle Collider { get; }

    /// <summary>Whether this is one cell of a grid collider rather than a shape collider.</summary>
    public bool IsGridCell { get; }

    /// <summary>The cell's column when <see cref="IsGridCell"/>; zero otherwise.</summary>
    public int CellX { get; }

    /// <summary>The cell's row when <see cref="IsGridCell"/>; zero otherwise.</summary>
    public int CellY { get; }

    /// <summary>The layer the collider or grid cell is on.</summary>
    public CollisionLayer Layer { get; }

    /// <summary>A target naming one collider.</summary>
    internal static CollisionTarget ForCollider(ColliderHandle collider, CollisionLayer layer) =>
        new(collider, false, 0, 0, layer);

    /// <summary>A target naming one cell of a grid collider.</summary>
    internal static CollisionTarget ForGridCell(ColliderHandle grid, int x, int y, CollisionLayer layer) =>
        new(grid, true, x, y, layer);
}
