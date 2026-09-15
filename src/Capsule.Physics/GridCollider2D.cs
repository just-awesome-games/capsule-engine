using System.Numerics;

namespace Capsule.Physics;

/// <summary>
/// One grid of layered cells, anchored at the world origin. Each cell is on the layer its palette
/// entry names and collides on the sides that entry declares. The grid is its own broadphase: a
/// query visits only the cells it crosses.
/// </summary>
public sealed class GridCollider2D
{
    private readonly int[] _cells;
    private readonly CollisionLayer?[] _layers;
    private readonly CellFaces2D[] _faces;

    // One byte a cell: whether it is a solid box, and which sides are surfaces a query can meet.
    // Derived once, so the mover's inner loop never walks back into the palette. A solid cell's
    // culling is purely geometric, so a query that filters part of the grid out re-decides it
    // through NeighbourAdmits; a cell with fewer than four faces keeps exactly what it declared.
    private readonly CellState[] _state;

    // Derived alongside _state so a query reads a colliding cell's layer in one indirection; the
    // entry of a cell that collides as nothing is never reached.
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
        _state = new CellState[cells.Length];
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

    // The union of the layers of the cells that actually collide: a query whose filter names none
    // of them skips this grid without walking a single cell.
    internal CollisionFilter Layers { get; private set; }

    /// <summary>
    /// The layer the cell at (<paramref name="x"/>, <paramref name="y"/>) is on, or null where it
    /// collides as nothing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public CollisionLayer? LayerAt(int x, int y)
    {
        RequireOnGrid(x, y);

        return _layers[_cells[(y * Width) + x]];
    }

    /// <summary>Which sides of the cell at (<paramref name="x"/>, <paramref name="y"/>) collide.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public CellFaces2D FacesAt(int x, int y)
    {
        RequireOnGrid(x, y);

        return _faces[_cells[(y * Width) + x]];
    }

    /// <summary>The world-space box of the cell at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public Aabb2D CellBounds(int x, int y)
    {
        RequireOnGrid(x, y);

        return CellBox(x, y);
    }

    internal CellState StateAt(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? _state[(y * Width) + x] : CellState.None;

    // The layer of a cell the caller has already found to collide: a cell whose palette entry names
    // no layer derives to CellState.None and never reaches a query.
    internal CollisionLayer LayerOf(int x, int y) => _cellLayers[(y * Width) + x];

    // Whether the derived face culling answers a query outright. It was derived over every cell of
    // the grid, so it holds only while the query can see every cell.
    internal bool AdmitsEveryLayer(CollisionFilter filter) => (Layers & filter) == Layers;

    // Whether the cell across a face is one the query both collides with and reads as solid. A cell
    // the filter excludes is empty space, so it shares no face.
    internal bool NeighbourAdmits(int x, int y, CellState face, CollisionFilter filter)
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

    // One side of a cell, as the zero-thickness box the narrowphase reads as a segment.
    internal Aabb2D FaceEdge(int x, int y, CellState face)
    {
        Aabb2D cell = CellBox(x, y);

        return face switch
        {
            CellState.FaceMinX => new Aabb2D(cell.Min, new Vector2(cell.Min.X, cell.Max.Y)),
            CellState.FaceMaxX => new Aabb2D(new Vector2(cell.Max.X, cell.Min.Y), cell.Max),
            CellState.FaceMinY => new Aabb2D(cell.Min, new Vector2(cell.Max.X, cell.Min.Y)),
            _ => new Aabb2D(new Vector2(cell.Min.X, cell.Max.Y), cell.Max),
        };
    }

    // The unit direction a face points away from its cell, and the normal a query meeting it
    // reports.
    internal static Vector2 FaceNormal(CellState face) => face switch
    {
        CellState.FaceMinX => new Vector2(-1f, 0f),
        CellState.FaceMaxX => new Vector2(1f, 0f),
        CellState.FaceMinY => new Vector2(0f, -1f),
        _ => new Vector2(0f, 1f),
    };

    // Which of a cell's four sides a normal names: the dominant axis, then its sign.
    internal static CellState FaceOf(Vector2 normal) =>
        MathF.Abs(normal.X) >= MathF.Abs(normal.Y)
            ? (normal.X < 0f ? CellState.FaceMinX : CellState.FaceMaxX)
            : (normal.Y < 0f ? CellState.FaceMinY : CellState.FaceMaxY);

    internal static int FloorDiv(float world, int cellSize) =>
        (int)MathF.Floor(world / cellSize);

    private static CellState FacesOf(CellFaces2D faces)
    {
        CellState state = CellState.None;

        if ((faces & CellFaces2D.Left) != 0)
        {
            state |= CellState.FaceMinX;
        }

        if ((faces & CellFaces2D.Right) != 0)
        {
            state |= CellState.FaceMaxX;
        }

        if ((faces & CellFaces2D.Top) != 0)
        {
            state |= CellState.FaceMinY;
        }

        if ((faces & CellFaces2D.Bottom) != 0)
        {
            state |= CellState.FaceMaxY;
        }

        return state;
    }

    private void DeriveCells()
    {
        CollisionFilter layers = CollisionFilter.None;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int index = (y * Width) + x;
                int palette = _cells[index];
                if (_layers[palette] is not { } layer)
                {
                    continue;
                }

                if (_faces[palette] != CellFaces2D.All)
                {
                    _state[index] = FacesOf(_faces[palette]);
                }
                else
                {
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

                _cellLayers[index] = layer;
                layers = layers.With(layer);
            }
        }

        Layers = layers;
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

[Flags]
internal enum CellState : byte
{
    None = 0,
    Solid = 1,
    FaceMinX = 2,
    FaceMaxX = 4,
    FaceMinY = 8,
    FaceMaxY = 16,
}
