using System.Numerics;

namespace Capsule.Physics;

/// <summary>
/// A grid of layered cells with cell (0, 0) at the world origin. Each cell sits on the layer its
/// palette entry names and collides on the sides that entry declares.
/// </summary>
/// <remarks>
/// A query visits only the cells it crosses. A scene's tile map builds one, and
/// <see cref="CollisionWorld2D.GridOf"/> finds it from a query's
/// <see cref="CollisionTarget.Collider"/>.
/// </remarks>
public sealed class GridCollider2D
{
    private readonly int[] _cells;
    private readonly CollisionLayer?[] _layers;
    private readonly CellFaces2D[] _faces;

    // One byte per cell, holding whether it is a solid box and which sides a query can meet. Derived
    // when the grid is built and when a cell changes, so the mover's inner loop never reads the
    // palette. Face culling on a solid cell is geometric, and a query that filters part of the grid
    // out re-decides it through NeighbourAdmits. A cell with fewer than four faces keeps what it
    // declared.
    private readonly CellState2D[] _state;

    // Derived alongside _state, giving a query the layer of a colliding cell in one indirection.
    // Entries for cells that collide as nothing are never read.
    private readonly CollisionLayer[] _cellLayers;

    internal GridCollider2D(
        ColliderHandle handle,
        int cellSize,
        int width,
        int height,
        int[] cells,
        CollisionLayer?[] layers,
        CellFaces2D[] faces)
    {
        Handle = handle;
        CellSize = cellSize;
        Width = width;
        Height = height;
        _cells = cells;
        _layers = layers;
        _faces = faces;
        _state = new CellState2D[cells.Length];
        _cellLayers = new CollisionLayer[cells.Length];

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

    // The union of the layers of cells that collide, or of cells that once did. A query whose filter
    // names none of them skips the grid without walking a cell.
    internal CollisionFilter Layers { get; private set; }

    /// <summary>
    /// The layer the cell at (<paramref name="x"/>, <paramref name="y"/>) is on, or null where it
    /// collides as nothing.
    /// </summary>
    public CollisionLayer? LayerAt(int x, int y)
    {
        RequireOnGrid(x, y);

        return _layers[_cells[(y * Width) + x]];
    }

    /// <summary>Which sides of the cell at (<paramref name="x"/>, <paramref name="y"/>) collide.</summary>
    public CellFaces2D FacesAt(int x, int y)
    {
        RequireOnGrid(x, y);

        return _faces[_cells[(y * Width) + x]];
    }

    /// <summary>The world-space box of the cell at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public Aabb2D CellBounds(int x, int y)
    {
        RequireOnGrid(x, y);

        return CellBox(x, y);
    }

    internal CellState2D StateAt(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? _state[(y * Width) + x] : CellState2D.None;

    // The layer of a cell the caller already found to collide. A cell whose palette entry names no
    // layer derives to CellState2D.None and never reaches a query.
    internal CollisionLayer LayerOf(int x, int y) => _cellLayers[(y * Width) + x];

    // Whether the derived face culling answers a query outright. Culling was derived over every cell
    // of the grid, so it holds only when the query can see every cell.
    internal bool AdmitsEveryLayer(CollisionFilter filter) => (Layers & filter) == Layers;

    // Whether the cell across a face is solid and passes the query's filter. A cell the filter
    // excludes counts as empty space and shares no face.
    internal bool NeighbourAdmits(int x, int y, CellState2D face, CollisionFilter filter)
    {
        Vector2 step = FaceNormal(face);
        x += (int)step.X;
        y += (int)step.Y;

        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return false;
        }

        int cell = _cells[(y * Width) + x];

        return _faces[cell] == CellFaces2D.All
            && _layers[cell] is { } layer
            && filter.Admits(layer);
    }

    internal Aabb2D CellBox(int x, int y) =>
        new(
            new Vector2(x * (float)CellSize, y * (float)CellSize),
            new Vector2((x + 1) * (float)CellSize, (y + 1) * (float)CellSize));

    // One side of a cell as a zero-thickness box, which the narrowphase reads as a segment.
    internal Aabb2D FaceEdge(int x, int y, CellState2D face)
    {
        Aabb2D cell = CellBox(x, y);

        return face switch
        {
            CellState2D.FaceMinX => new Aabb2D(cell.Min, new Vector2(cell.Min.X, cell.Max.Y)),
            CellState2D.FaceMaxX => new Aabb2D(new Vector2(cell.Max.X, cell.Min.Y), cell.Max),
            CellState2D.FaceMinY => new Aabb2D(cell.Min, new Vector2(cell.Max.X, cell.Min.Y)),
            _ => new Aabb2D(new Vector2(cell.Min.X, cell.Max.Y), cell.Max),
        };
    }

    // The unit direction a face points away from its cell, and the normal a query meeting it reports.
    internal static Vector2 FaceNormal(CellState2D face) => face switch
    {
        CellState2D.FaceMinX => new Vector2(-1f, 0f),
        CellState2D.FaceMaxX => new Vector2(1f, 0f),
        CellState2D.FaceMinY => new Vector2(0f, -1f),
        _ => new Vector2(0f, 1f),
    };

    // Which of a cell's four sides a normal names, by dominant axis then sign.
    internal static CellState2D FaceOf(Vector2 normal) =>
        MathF.Abs(normal.X) >= MathF.Abs(normal.Y)
            ? (normal.X < 0f ? CellState2D.FaceMinX : CellState2D.FaceMaxX)
            : (normal.Y < 0f ? CellState2D.FaceMinY : CellState2D.FaceMaxY);

    // Clamped to the int range. A far-out coordinate pins to one end of the axis instead of wrapping
    // to the other and inverting the cell range.
    internal static int FloorDiv(float world, int cellSize)
    {
        float cell = MathF.Floor(world / cellSize);

        return cell < int.MinValue ? int.MinValue : cell >= int.MaxValue ? int.MaxValue : (int)cell;
    }

    private static CellState2D FacesOf(CellFaces2D faces)
    {
        CellState2D state = CellState2D.None;

        if ((faces & CellFaces2D.Left) != 0)
        {
            state |= CellState2D.FaceMinX;
        }

        if ((faces & CellFaces2D.Right) != 0)
        {
            state |= CellState2D.FaceMaxX;
        }

        if ((faces & CellFaces2D.Top) != 0)
        {
            state |= CellState2D.FaceMinY;
        }

        if ((faces & CellFaces2D.Bottom) != 0)
        {
            state |= CellState2D.FaceMaxY;
        }

        return state;
    }

    // Writes one cell's palette index and re-derives it and its four neighbours, whose face culling
    // reads it. The layer union only widens. A superset still skips no query it should answer.
    internal void SetCell(int x, int y, int palette)
    {
        RequireOnGrid(x, y);
        ArgumentOutOfRangeException.ThrowIfNegative(palette);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(palette, _layers.Length);

        int index = (y * Width) + x;
        if (_cells[index] == palette)
        {
            return;
        }

        _cells[index] = palette;
        DeriveCell(x, y);
        DeriveCell(x - 1, y);
        DeriveCell(x + 1, y);
        DeriveCell(x, y - 1);
        DeriveCell(x, y + 1);
    }

    private void DeriveCells()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                DeriveCell(x, y);
            }
        }
    }

    private void DeriveCell(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        int index = (y * Width) + x;
        int palette = _cells[index];
        if (_layers[palette] is not { } layer)
        {
            _state[index] = CellState2D.None;
            return;
        }

        if (_faces[palette] != CellFaces2D.All)
        {
            _state[index] = FacesOf(_faces[palette]);
        }
        else
        {
            CellState2D state = CellState2D.Solid;
            if (!IsSolid(x - 1, y))
            {
                state |= CellState2D.FaceMinX;
            }

            if (!IsSolid(x + 1, y))
            {
                state |= CellState2D.FaceMaxX;
            }

            if (!IsSolid(x, y - 1))
            {
                state |= CellState2D.FaceMinY;
            }

            if (!IsSolid(x, y + 1))
            {
                state |= CellState2D.FaceMaxY;
            }

            _state[index] = state;
        }

        _cellLayers[index] = layer;
        Layers = Layers.With(layer);
    }

    private bool IsSolid(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return false;
        }

        int cell = _cells[(y * Width) + x];

        return _faces[cell] == CellFaces2D.All && _layers[cell] is not null;
    }

    private void RequireOnGrid(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
    }
}

// What a derived cell collides as, a solid box plus the sides a query can meet.
[Flags]
internal enum CellState2D : byte
{
    None = 0,
    Solid = 1,
    FaceMinX = 2,
    FaceMaxX = 4,
    FaceMinY = 8,
    FaceMaxY = 16,
}
