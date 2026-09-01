using System.Numerics;

namespace Capsule.Collision;

/// <summary>
/// One grid of collidable cells, anchored at the world origin. The grid is its own broadphase, so
/// a query visits only the cells it crosses rather than a list of individual colliders. It holds
/// cell indices, collision kinds and tags, and knows nothing about what authored those cells.
/// </summary>
public sealed class GridCollider2D
{
    private readonly int[] _cells;
    private readonly CellCollision[] _kinds;
    private readonly CollisionTag[] _tags;

    // One byte a cell: what it collides as, and which of its faces are not shared with a
    // neighbour that collides the same way. Derived once, so the mover's inner loop reads a
    // boundary face without walking back into the palette. Purely geometric: a query that filters
    // some of the grid out re-decides the faces it culled, through NeighbourAdmits.
    private readonly CellState[] _state;

    internal GridCollider2D(
        ColliderHandle handle,
        int cellSize,
        int width,
        int height,
        int[] cells,
        CellCollision[] kinds,
        CollisionTag[] tags)
    {
        Handle = handle;
        CellSize = cellSize;
        Width = width;
        Height = height;
        _cells = cells;
        _kinds = kinds;
        _tags = tags;
        _state = new CellState[cells.Length];

        Bounds = new Aabb2D(Vector2.Zero, new Vector2(width * (float)cellSize, height * (float)cellSize));

        DeriveCells();
    }

    /// <summary>This collider's identity in its world.</summary>
    public ColliderHandle Handle { get; }

    /// <summary>World units a cell spans on each axis.</summary>
    public int CellSize { get; }

    /// <summary>Cells across.</summary>
    public int Width { get; }

    /// <summary>Cells down.</summary>
    public int Height { get; }

    /// <summary>The world region the grid covers, from the origin.</summary>
    public Aabb2D Bounds { get; }

    // The union of the palette's tags: a query whose filter shares none of them skips this grid
    // without walking a single cell.
    internal CollisionFilter Tags { get; private set; }

    /// <summary>What the cell at (<paramref name="x"/>, <paramref name="y"/>) collides as.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public CellCollision CollisionAt(int x, int y)
    {
        RequireOnGrid(x, y);

        return _kinds[_cells[(y * Width) + x]];
    }

    /// <summary>The tag the cell at (<paramref name="x"/>, <paramref name="y"/>) carries.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public CollisionTag TagAt(int x, int y)
    {
        RequireOnGrid(x, y);

        return _tags[_cells[(y * Width) + x]];
    }

    /// <summary>The world-space box of the cell at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public Aabb2D CellBounds(int x, int y)
    {
        RequireOnGrid(x, y);

        return CellBox(x, y);
    }

    /// <summary>The cell holding <paramref name="world"/>, whether or not the grid has such a cell.</summary>
    public (int X, int Y) CellAt(Vector2 world) => (FloorDiv(world.X, CellSize), FloorDiv(world.Y, CellSize));

    internal CellState StateAt(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? _state[(y * Width) + x] : CellState.None;

    internal CollisionTag TagOf(int x, int y) => _tags[_cells[(y * Width) + x]];

    // Whether the derived face culling answers a query outright. Culling was derived over every
    // cell in the grid, so it is the query's answer only while the query can see every cell.
    internal bool AdmitsEveryTag(CollisionFilter filter) => (Tags & filter) == Tags;

    // Whether the cell across the face a normal points out of is one the query both collides with
    // and reads as solid. A cell the filter excludes is empty space, so it shares no face.
    internal bool NeighbourAdmits(int x, int y, Vector2 normal, CollisionFilter filter)
    {
        if (MathF.Abs(normal.X) >= MathF.Abs(normal.Y))
        {
            x += normal.X < 0f ? -1 : 1;
        }
        else
        {
            y += normal.Y < 0f ? -1 : 1;
        }

        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return false;
        }

        int cell = _cells[(y * Width) + x];

        return _kinds[cell] == CellCollision.Solid && filter.Matches(_tags[cell]);
    }

    internal Aabb2D CellBox(int x, int y) =>
        new(
            new Vector2(x * (float)CellSize, y * (float)CellSize),
            new Vector2((x + 1) * (float)CellSize, (y + 1) * (float)CellSize));

    // The one-way edge, as the degenerate box the narrowphase treats it as: the cell's low-Y face.
    internal Aabb2D OneWayEdge(int x, int y)
    {
        float top = y * (float)CellSize;

        return new Aabb2D(new Vector2(x * (float)CellSize, top), new Vector2((x + 1) * (float)CellSize, top));
    }

    internal static int FloorDiv(float world, int cellSize) =>
        (int)MathF.Floor(world / cellSize);

    private void DeriveCells()
    {
        CollisionFilter tags = CollisionFilter.None;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int index = (y * Width) + x;
                CellCollision kind = _kinds[_cells[index]];
                if (kind == CellCollision.None)
                {
                    continue;
                }

                tags = tags.With(_tags[_cells[index]]);

                if (kind == CellCollision.OneWay)
                {
                    // A one-way edge is one segment however its neighbours are painted: adjacent
                    // edges are collinear, so no seam can catch, and it never blocks along X.
                    _state[index] = CellState.OneWay | CellState.FaceMinY;
                    continue;
                }

                CellState state = CellState.Solid;
                if (!IsSolid(x - 1, y))
                {
                    state |= CellState.FaceMinX;
                }

                if (!IsSolid(x + 1, y))
                {
                    state |= CellState.FaceMaxX;
                }

                if (!IsSolid(x, y - 1))
                {
                    state |= CellState.FaceMinY;
                }

                if (!IsSolid(x, y + 1))
                {
                    state |= CellState.FaceMaxY;
                }

                _state[index] = state;
            }
        }

        Tags = tags;
    }

    private bool IsSolid(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height
        && _kinds[_cells[(y * Width) + x]] == CellCollision.Solid;

    private void RequireOnGrid(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
    }
}

[Flags]
internal enum CellState : byte
{
    None = 0,
    Solid = 1,
    OneWay = 2,
    FaceMinX = 4,
    FaceMaxX = 8,
    FaceMinY = 16,
    FaceMaxY = 32,
}
