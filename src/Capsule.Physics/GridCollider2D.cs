using System.Numerics;

namespace Capsule.Physics;

/// <summary>
/// A grid of layered cells with cell (0, 0) at the world origin. Each cell sits on the layer its
/// palette entry names and collides as that entry's shape.
/// </summary>
/// <remarks>
/// A query visits only the cells it crosses. A scene's tile map builds one, and
/// <see cref="CollisionWorld2D.GridOf"/> finds it from a query's
/// <see cref="CollisionTarget.Collider"/>.
/// </remarks>
public sealed class GridCollider2D
{
    // The most edges one palette entry derives. A polygon holds at most Shape2D.MaxPoints corners.
    internal const int MaxEdges = Shape2D.MaxPoints;

    // How far below zero the Y of an outward normal must reach for the edge to face up. A one-way
    // surface blocks only there.
    internal const float UpFacing = 1e-4f;

    private readonly int[] _cells;
    private readonly CollisionLayer?[] _layers;

    // Per palette entry. A solid full-cell box takes the closed-form path and derives no edges. Every
    // other colliding entry is MaxEdges slots of _edges, of which _edgeCounts says how many are used.
    private readonly bool[] _boxes;
    private readonly bool[] _oneWay;
    private readonly bool[] _solidSides;
    private readonly int[] _edgeCounts;
    private readonly CellEdge2D[] _edges;

    // The solid polygon of each entry in cell space, read by overlap queries. Default for a box, a
    // one-way entry and an entry that collides as nothing.
    private readonly Shape2D[] _polygons;

    // Four per entry, one per cell side in SideIndex order: the stretch of that side the entry covers
    // with a solid shape. An edge a neighbour lies flush against within a covered stretch is culled.
    private readonly SideSpan2D[] _covers;

    // The same for the left and right walls of a solid-sided one-way entry. Only another such entry
    // culls its own wall against them.
    private readonly SideSpan2D[] _walls;

    // One byte per cell, holding what it collides as and which of its sides or edges a query can
    // meet. Derived when the grid is built and when a cell changes, so the mover's inner loop never
    // reads the palette for a box. Culling is geometric, and a query that filters part of the grid
    // out re-decides it through NeighbourAdmits.
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
        ReadOnlySpan<CellProfile2D> profiles)
    {
        Handle = handle;
        CellSize = cellSize;
        Width = width;
        Height = height;
        _cells = cells;

        int count = profiles.Length;
        _layers = new CollisionLayer?[count];
        _boxes = new bool[count];
        _oneWay = new bool[count];
        _solidSides = new bool[count];
        _edgeCounts = new int[count];
        _edges = new CellEdge2D[count * MaxEdges];
        _polygons = new Shape2D[count];
        _covers = new SideSpan2D[count * 4];
        _walls = new SideSpan2D[count * 4];
        Array.Fill(_covers, SideSpan2D.None);
        Array.Fill(_walls, SideSpan2D.None);
        for (int index = 0; index < count; index++)
        {
            DerivePalette(index, profiles[index]);
        }

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

    // The edges of an edge cell in cell space, live or culled. StateAt says which are live.
    internal ReadOnlySpan<CellEdge2D> EdgesAt(int x, int y)
    {
        int palette = _cells[(y * Width) + x];

        return _edges.AsSpan(palette * MaxEdges, _edgeCounts[palette]);
    }

    // The solid polygon of an edge cell in cell space. Only a cell without the OneWay bit has one.
    internal Shape2D PolygonAt(int x, int y) => _polygons[_cells[(y * Width) + x]];

    // The world position of a cell's top-left corner, the origin of its cell space.
    internal Vector2 CellCorner(int x, int y) => new(x * (float)CellSize, y * (float)CellSize);

    // Whether the derived face culling answers a query outright. Culling was derived over every cell
    // of the grid, so it holds only when the query can see every cell.
    internal bool AdmitsEveryLayer(CollisionFilter filter) => (Layers & filter) == Layers;

    // Whether the cell across a side covers it fully with a solid shape and passes the query's
    // filter. A cell the filter excludes counts as empty space and shares no side.
    internal bool NeighbourAdmits(int x, int y, CellState2D face, CollisionFilter filter) =>
        CoverAcross(x, y, face, 0f, CellSize) is { } layer && filter.Admits(layer);

    // Whether edge `index` of an edge cell is a surface this query can meet. An edge culled against a
    // neighbour the filter excludes is live again.
    internal bool EdgeLive(int x, int y, CellState2D state, int index, in CellEdge2D edge, CollisionFilter filter, bool admitsEvery) =>
        (state & EdgeBit(index)) != 0
        || (!admitsEvery
            && edge.Side != CellState2D.None
            && !(CoverAcross(x, y, edge.Side, edge.Low, edge.High) is { } layer && filter.Admits(layer)));

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

    // The bit of an edge cell's state that marks edge `index` live. It shares the face bits, which
    // only a box cell reads.
    internal static CellState2D EdgeBit(int index) => (CellState2D)(2 << index);

    // The unit direction a face points away from its cell, and the normal a query meeting it reports.
    internal static Vector2 FaceNormal(CellState2D face) => face switch
    {
        CellState2D.FaceMinX => new Vector2(-1f, 0f),
        CellState2D.FaceMaxX => new Vector2(1f, 0f),
        CellState2D.FaceMinY => new Vector2(0f, -1f),
        _ => new Vector2(0f, 1f),
    };

    // Whether a one-way surface keeps an edge with this outward normal. A plain one keeps only what faces
    // up, and a solid-sided one everything but what faces down.
    internal static bool OneWayKeeps(Vector2 normal, bool solidSides) =>
        solidSides ? !(normal.Y > UpFacing) : normal.Y < -UpFacing;

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

    // Writes one cell's palette index and re-derives it and its four neighbours, whose culling reads
    // it. The layer union only widens. A superset still skips no query it should answer.
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

    private static CellState2D Opposite(CellState2D face) => face switch
    {
        CellState2D.FaceMinX => CellState2D.FaceMaxX,
        CellState2D.FaceMaxX => CellState2D.FaceMinX,
        CellState2D.FaceMinY => CellState2D.FaceMaxY,
        _ => CellState2D.FaceMinY,
    };

    // Derives an entry's edges once, each with its outward normal and the cell side it spans, if any.
    private void DerivePalette(int index, in CellProfile2D profile)
    {
        _layers[index] = profile.Layer;
        _oneWay[index] = profile.OneWay;
        _solidSides[index] = profile.SolidSides;

        if (profile.Layer is null)
        {
            return;
        }

        float size = CellSize;
        Shape2D shape = profile.Shape ?? Shape2D.Box(new Aabb2D(Vector2.Zero, new Vector2(size, size)));
        bool fullBox = shape.Kind == ShapeKind2D.Box
            && shape.Bounds.Min == Vector2.Zero
            && shape.Bounds.Max == new Vector2(size, size);

        if (fullBox && !profile.OneWay)
        {
            _boxes[index] = true;
            _covers.AsSpan(index * 4, 4).Fill(new SideSpan2D(0f, size));
            return;
        }

        int count = 0;
        for (int corner = 0; corner < shape.PointCount; corner++)
        {
            Vector2 start = shape.PointAt(corner);
            Vector2 end = shape.PointAt((corner + 1) % shape.PointCount);
            Vector2 along = end - start;
            Vector2 normal = Vector2.Normalize(new Vector2(along.Y, -along.X));

            if (profile.OneWay && !OneWayKeeps(normal, profile.SolidSides))
            {
                continue;
            }

            Vector2 min = Vector2.Min(start, end);
            Vector2 max = Vector2.Max(start, end);
            CellState2D side = SideOf(min, max, size);
            bool vertical = side is CellState2D.FaceMinX or CellState2D.FaceMaxX;
            SideSpan2D span = vertical ? new SideSpan2D(min.Y, max.Y) : new SideSpan2D(min.X, max.X);
            if (side != CellState2D.None && !profile.OneWay)
            {
                _covers[(index * 4) + SideIndex(side)] = span;
            }
            else if (vertical && profile.SolidSides)
            {
                _walls[(index * 4) + SideIndex(side)] = span;
            }

            _edges[(index * MaxEdges) + count] = new CellEdge2D(start, end, normal, side, span.Low, span.High);
            count++;
        }

        _edgeCounts[index] = count;
        if (!profile.OneWay)
        {
            _polygons[index] = shape;
        }
    }

    // The cell side an axis-aligned edge lies on, or None.
    private static CellState2D SideOf(Vector2 min, Vector2 max, float size)
    {
        if (min.X == max.X)
        {
            return min.X == 0f ? CellState2D.FaceMinX : min.X == size ? CellState2D.FaceMaxX : CellState2D.None;
        }

        if (min.Y == max.Y)
        {
            return min.Y == 0f ? CellState2D.FaceMinY : min.Y == size ? CellState2D.FaceMaxY : CellState2D.None;
        }

        return CellState2D.None;
    }

    // A side's slot among an entry's four spans: MinX, MaxX, MinY, MaxY.
    private static int SideIndex(CellState2D face) => BitOperations.TrailingZeroCount((uint)face) - 1;

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

        CellState2D state;
        if (_boxes[palette])
        {
            state = CellState2D.Solid;
            state |= Exposed(x, y, CellState2D.FaceMinX);
            state |= Exposed(x, y, CellState2D.FaceMaxX);
            state |= Exposed(x, y, CellState2D.FaceMinY);
            state |= Exposed(x, y, CellState2D.FaceMaxY);
        }
        else
        {
            state = !_oneWay[palette] ? CellState2D.Edges
                : _solidSides[palette] ? CellState2D.Edges | CellState2D.OneWay | CellState2D.SolidSides
                : CellState2D.Edges | CellState2D.OneWay;
            ReadOnlySpan<CellEdge2D> edges = _edges.AsSpan(palette * MaxEdges, _edgeCounts[palette]);
            for (int edge = 0; edge < edges.Length; edge++)
            {
                CellEdge2D derived = edges[edge];
                if (derived.Side == CellState2D.None || CoverAcross(x, y, derived.Side, derived.Low, derived.High) is null)
                {
                    state |= EdgeBit(edge);
                }
            }
        }

        _state[index] = state;
        _cellLayers[index] = layer;
        Layers = Layers.With(layer);
    }

    // The face itself when no neighbour covers it with a solid shape, and None when one does.
    private CellState2D Exposed(int x, int y, CellState2D face) =>
        CoverAcross(x, y, face, 0f, CellSize) is null ? face : CellState2D.None;

    // The layer of the cell across a side when that cell covers the stretch from `low` to `high` of it
    // with a solid shape, or null. Only a colliding entry covers a side. A solid-sided one-way cell also
    // counts the wall of a neighbour like it. Derived culling and a filtered query's re-decision both
    // ask here.
    private CollisionLayer? CoverAcross(int x, int y, CellState2D face, float low, float high)
    {
        bool solidSides = _solidSides[_cells[(y * Width) + x]];
        Vector2 step = FaceNormal(face);
        x += (int)step.X;
        y += (int)step.Y;

        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return null;
        }

        int cell = _cells[(y * Width) + x];
        int slot = (cell * 4) + SideIndex(Opposite(face));

        return _covers[slot].Covers(low, high) || (solidSides && _walls[slot].Covers(low, high)) ? _layers[cell] : null;
    }

    private void RequireOnGrid(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
    }
}

// What a derived cell collides as. A solid box carries Solid and the sides a query can meet. Any other
// colliding cell carries Edges, OneWay when it blocks only from above, SolidSides when it also blocks
// from the sides, and EdgeBit(i) for each live edge, which reuses the face bits.
[Flags]
internal enum CellState2D : byte
{
    None = 0,
    Solid = 1,
    FaceMinX = 2,
    FaceMaxX = 4,
    FaceMinY = 8,
    FaceMaxY = 16,
    Edges = 32,
    OneWay = 64,
    SolidSides = 128,
}

// One edge of a palette entry in cell space, with its outward normal, the cell side it lies on or None,
// and the stretch of that side it spans.
internal readonly record struct CellEdge2D(Vector2 Start, Vector2 End, Vector2 Normal, CellState2D Side, float Low, float High);

// A stretch of one cell side in cell space, along Y for a left or right side and along X otherwise.
internal readonly record struct SideSpan2D(float Low, float High)
{
    // Covers nothing, not even a point.
    internal static readonly SideSpan2D None = new(float.PositiveInfinity, float.NegativeInfinity);

    internal bool Covers(float low, float high) => Low <= low && High >= high;
}
