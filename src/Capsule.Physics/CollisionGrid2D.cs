using System.Numerics;

namespace Capsule.Physics;

// A grid of layered cells with cell (0, 0) at the world origin. Each cell sits on the layer its
// profile names and collides as that profile's shape. A query visits only the cells it crosses. A
// tile map's collider builds one, and CollisionWorld2D.GridOf finds it from a query's
// CollisionTarget.Collider.
// The grid reads its owner's cells in place and never writes them. A cell's profile is the lookup entry its
// value and variant name. The owner writes a cell and then calls Refresh.
internal sealed class CollisionGrid2D
{
    // How far below zero the Y of an outward normal must reach for the edge to face up. A one-way
    // surface blocks only there.
    internal const float UpFacing = 1e-4f;

    // The profile a derivation reads across a side past the grid's edge, where nothing covers it.
    private const int OffGrid = -1;

    // How many profiles the derivation's three rows hold on the stack. A wider grid takes them from the
    // heap once.
    private const int RowsOnStack = 2048;

    // The owner's cells, and the profile each value and variant names at (value * _stride) + variant.
    private readonly int[] _lookup;
    private readonly int _stride;
    private CellBlocks _cells;

    private readonly CollisionLayer?[] _layers;

    // Per palette entry. A solid full-cell box takes the closed-form path and derives no edges. Every
    // other colliding entry has a slot in _edges per corner from _edgeStarts on, of which _edgeCounts says
    // how many are used.
    private readonly bool[] _boxes;
    private readonly bool[] _oneWay;
    private readonly bool[] _solidSides;
    private readonly int[] _edgeStarts;
    private readonly int[] _edgeCounts;
    private readonly CellEdge2D[] _edges;

    // The solid polygons of the entries in cell space, read by overlap queries, and each entry's index
    // among them. Index 0 holds the default shape, which a box, a one-way entry and an entry that
    // collides as nothing name.
    private readonly Shape2D[] _polygons;
    private readonly int[] _polygonOf;

    // Four per entry, one per cell side in SideIndex order: the stretch of that side the entry covers
    // with a solid shape. An edge a neighbour lies flush against within a covered stretch is culled.
    private readonly SideSpan2D[] _covers;

    // Per entry, the face bits of the sides its solid shape covers whole, which a box cell's derivation
    // reads in place of the spans.
    private readonly CellState2D[] _wholeSides;

    // The same for the left and right walls of a solid-sided one-way entry. Only another such entry
    // culls its own wall against them, and a palette with none holds no walls.
    private readonly SideSpan2D[] _walls;

    // One byte per cell, holding what it collides as and which of its sides or edges a query can
    // meet. It is derived when the grid is built and when a cell changes, and the mover's inner loop
    // never reads the palette for a box. Culling is geometric, and a query that filters part of the
    // grid out re-decides it through NeighbourAdmits.
    private readonly CellState2D[] _state;

    // This grid's identity in its world.
    internal ColliderHandle Handle { get; }

    // World units a cell spans on each axis.
    internal int CellSize { get; }

    // Cells across.
    internal int Width { get; }

    // Cells down.
    internal int Height { get; }

    // The world region the grid covers, from the origin.
    internal Aabb2D Bounds { get; }

    // The union of the layers of cells that collide, or of cells that once did. A query whose filter
    // names none of them skips the grid without walking a cell.
    internal CollisionFilter Layers { get; private set; }

    internal CollisionGrid2D(
        ColliderHandle handle,
        int cellSize,
        CellBlocks cells,
        int[] lookup,
        int stride,
        ReadOnlySpan<CellProfile2D> profiles)
    {
        Handle = handle;
        CellSize = cellSize;
        Width = cells.Width;
        Height = cells.Height;
        _cells = cells;
        _lookup = lookup;
        _stride = stride;

        int count = profiles.Length;
        _layers = new CollisionLayer?[count];
        _boxes = new bool[count];
        _oneWay = new bool[count];
        _solidSides = new bool[count];
        _edgeStarts = new int[count];
        _edgeCounts = new int[count];
        int edgeSlots = 0;
        int polygons = 1;
        bool walled = false;
        for (int index = 0; index < count; index++)
        {
            _edgeStarts[index] = edgeSlots;
            CellProfile2D profile = profiles[index];
            if (profile.Layer is null)
            {
                continue;
            }

            // An entry that collides as the solid full cell derives no edges.
            edgeSlots += profile.Shape?.PointCount ?? (profile.OneWay ? 4 : 0);
            polygons += profile.Shape is not null && !profile.OneWay ? 1 : 0;
            walled |= profile.SolidSides;
        }

        _edges = new CellEdge2D[edgeSlots];
        _polygons = new Shape2D[polygons];
        _polygonOf = new int[count];
        _covers = new SideSpan2D[count * 4];
        _walls = walled ? new SideSpan2D[count * 4] : [];
        Array.Fill(_covers, SideSpan2D.None);
        Array.Fill(_walls, SideSpan2D.None);
        _wholeSides = new CellState2D[count];
        polygons = 1;
        for (int index = 0; index < count; index++)
        {
            DerivePalette(index, profiles[index], ref polygons);
            for (int side = 0; side < 4; side++)
            {
                if (_covers[(index * 4) + side].Covers(0f, cellSize))
                {
                    _wholeSides[index] |= (CellState2D)(2 << side);
                }
            }
        }

        _state = new CellState2D[Width * Height];

        Bounds = new Aabb2D(Vector2.Zero, new Vector2(Width * (float)cellSize, Height * (float)cellSize));

        DeriveCells();
    }

    // Every cell's state, row by row. A cell's index is y * Width + x.
    internal ReadOnlySpan<CellState2D> States => _state;

    internal CellState2D StateAt(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? _state[(y * Width) + x] : CellState2D.None;

    // The layer of a cell the caller already found to collide. A cell whose palette entry names no
    // layer derives to CellState2D.None and never reaches a query.
    internal CollisionLayer LayerOf(int x, int y) => _layers[Profile(x, y)].GetValueOrDefault();

    // The edges of an edge cell in cell space, live or culled. StateAt says which are live.
    internal ReadOnlySpan<CellEdge2D> EdgesAt(int x, int y)
    {
        int profile = Profile(x, y);

        return _edges.AsSpan(_edgeStarts[profile], _edgeCounts[profile]);
    }

    // The solid polygon of an edge cell in cell space. A one-way cell has none, and neither has a solid
    // full-cell box, which is a box cell and not an edge cell.
    internal Shape2D PolygonAt(int x, int y) => _polygons[_polygonOf[Profile(x, y)]];

    // The world position of a cell's top-left corner, the origin of its cell space.
    internal Vector2 CellCorner(int x, int y) => new(x * (float)CellSize, y * (float)CellSize);

    // Whether the derived face culling answers a query outright. Culling was derived over every cell
    // of the grid and holds only when the query can see every cell.
    internal bool AdmitsEveryLayer(CollisionFilter filter) => (Layers & filter) == Layers;

    // Whether the cell across a side covers it fully with a solid shape and passes the query's
    // filter. A cell the filter excludes counts as empty space and shares no side.
    internal bool NeighbourAdmits(int x, int y, CellState2D face, CollisionFilter filter) =>
        CoverAcross(x, y, face, 0f, CellSize) is { } layer && filter.Admits(layer);

    // Whether an edge of an edge cell is a surface this query can meet. Only an edge on a cell side is
    // ever culled, and an edge culled against a neighbour the filter excludes is live again.
    internal bool EdgeLive(int x, int y, CellState2D state, in CellEdge2D edge, CollisionFilter filter, bool admitsEvery) =>
        Live(state, edge)
        || (!admitsEvery && !(CoverAcross(x, y, edge.Side, edge.Low, edge.High) is { } layer && filter.Admits(layer)));

    // Whether an edge survived the culling derived over every cell of the grid.
    internal static bool Live(CellState2D state, in CellEdge2D edge) => edge.Side == CellState2D.None || (state & edge.Side) != 0;

    internal Aabb2D CellBox(int x, int y) => new(CellCorner(x, y), CellCorner(x + 1, y + 1));

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

    // Re-derives one cell the owner wrote and its four neighbours, whose culling reads it. The layer union only
    // widens. A superset still skips no query it should answer.
    internal void Refresh(int x, int y)
    {
        RequireOnGrid(x, y);

        DeriveCell(x, y);
        DeriveCell(x - 1, y);
        DeriveCell(x + 1, y);
        DeriveCell(x, y - 1);
        DeriveCell(x, y + 1);
    }

    // Points the grid at new storage holding the owner's cells.
    internal void Rebind(CellBlocks cells) => _cells = cells;

    private static CellState2D Opposite(CellState2D face) => face switch
    {
        CellState2D.FaceMinX => CellState2D.FaceMaxX,
        CellState2D.FaceMaxX => CellState2D.FaceMinX,
        CellState2D.FaceMinY => CellState2D.FaceMaxY,
        _ => CellState2D.FaceMinY,
    };

    // Derives an entry's edges once, each with its outward normal and the cell side it spans, if any.
    private void DerivePalette(int index, in CellProfile2D profile, ref int polygons)
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
            Vector2 normal = shape.EdgeNormal(corner);

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

            _edges[_edgeStarts[index] + count] = new CellEdge2D(start, end, normal, side, span.Low, span.High);
            count++;
        }

        _edgeCounts[index] = count;
        if (!profile.OneWay)
        {
            _polygonOf[index] = polygons;
            _polygons[polygons++] = shape;
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

    // Row by row, reading each cell's key once. Three rows of profiles slide down the grid: the row
    // above, the row being derived and the row below.
    private void DeriveCells()
    {
        int width = Width;
        Span<int> rows = 3 * width <= RowsOnStack ? stackalloc int[3 * width] : new int[3 * width];
        int above = 0;
        int at = width;
        int below = 2 * width;
        ReadRow(0, rows.Slice(at, width));

        for (int y = 0; y < Height; y++)
        {
            bool last = y == Height - 1;
            if (!last)
            {
                ReadRow(y + 1, rows.Slice(below, width));
            }

            for (int x = 0; x < width; x++)
            {
                int profile = rows[at + x];
                CellState2D state = Derive(
                    profile,
                    x > 0 ? rows[at + x - 1] : OffGrid,
                    x < width - 1 ? rows[at + x + 1] : OffGrid,
                    y > 0 ? rows[above + x] : OffGrid,
                    last ? OffGrid : rows[below + x]);
                _state[(y * width) + x] = state;

                if (state == CellState2D.None)
                {
                    continue;
                }

                // With validates the layer, which a crowd of cells on one layer then pays once.
                CollisionLayer layer = _layers[profile].GetValueOrDefault();
                if (!Layers.Admits(layer))
                {
                    Layers = Layers.With(layer);
                }
            }

            (above, at, below) = (at, below, above);
        }
    }

    private void ReadRow(int y, Span<int> row)
    {
        _cells.KeysInRow(y, _stride, row);
        foreach (ref int entry in row)
        {
            entry = _lookup[entry];
        }
    }

    private void DeriveCell(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        int profile = Profile(x, y);
        CellState2D state = Derive(
            profile,
            ProfileOrOff(x - 1, y),
            ProfileOrOff(x + 1, y),
            ProfileOrOff(x, y - 1),
            ProfileOrOff(x, y + 1));
        _state[(y * Width) + x] = state;

        if (state != CellState2D.None)
        {
            Layers = Layers.With(_layers[profile].GetValueOrDefault());
        }
    }

    // What a cell of the profile collides as, from the profiles across its four sides, each OffGrid
    // past the grid's edge.
    private CellState2D Derive(int profile, int minX, int maxX, int minY, int maxY)
    {
        if (_layers[profile] is null)
        {
            return CellState2D.None;
        }

        if (_boxes[profile])
        {
            return CellState2D.Solid
                | Exposed(minX, CellState2D.FaceMinX)
                | Exposed(maxX, CellState2D.FaceMaxX)
                | Exposed(minY, CellState2D.FaceMinY)
                | Exposed(maxY, CellState2D.FaceMaxY);
        }

        bool solidSides = _solidSides[profile];
        CellState2D state = !_oneWay[profile] ? CellState2D.Edges
            : solidSides ? CellState2D.Edges | CellState2D.OneWay | CellState2D.SolidSides
            : CellState2D.Edges | CellState2D.OneWay;
        foreach (CellEdge2D edge in _edges.AsSpan(_edgeStarts[profile], _edgeCounts[profile]))
        {
            if (edge.Side == CellState2D.None)
            {
                continue;
            }

            int across = edge.Side switch
            {
                CellState2D.FaceMinX => minX,
                CellState2D.FaceMaxX => maxX,
                CellState2D.FaceMinY => minY,
                _ => maxY,
            };

            if (CoverBy(across, edge.Side, edge.Low, edge.High, solidSides) is null)
            {
                state |= edge.Side;
            }
        }

        return state;
    }

    // A box cell's face itself when the profile across it does not cover it whole with a solid shape,
    // and None when it does. Only an edge cell can be one-way, and a box cell never counts a wall.
    private CellState2D Exposed(int across, CellState2D face) =>
        across != OffGrid && (_wholeSides[across] & Opposite(face)) != 0 ? CellState2D.None : face;

    // The layer of the cell across a side when that cell covers the stretch from `low` to `high` of it
    // with a solid shape, or null. Only a colliding entry covers a side. A solid-sided one-way cell also
    // counts the wall of a neighbour like it. Derived culling and a filtered query's re-decision both
    // ask here.
    private CollisionLayer? CoverAcross(int x, int y, CellState2D face, float low, float high)
    {
        Vector2 step = FaceNormal(face);

        return CoverBy(ProfileOrOff(x + (int)step.X, y + (int)step.Y), face, low, high, _solidSides[Profile(x, y)]);
    }

    // CoverAcross for the profile across the side, OffGrid past the grid's edge. solidSides says
    // whether the cell asking is a solid-sided one-way cell.
    private CollisionLayer? CoverBy(int across, CellState2D face, float low, float high, bool solidSides)
    {
        if (across == OffGrid)
        {
            return null;
        }

        int slot = (across * 4) + SideIndex(Opposite(face));

        return _covers[slot].Covers(low, high) || (solidSides && _walls[slot].Covers(low, high)) ? _layers[across] : null;
    }

    private int ProfileOrOff(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? Profile(x, y) : OffGrid;

    private int Profile(int x, int y) => _lookup[_cells.KeyAt(x, y, _stride)];

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
// from the sides, and the face bit of each side whose edge is live. A strictly convex shape has at most
// one edge on each side.
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
