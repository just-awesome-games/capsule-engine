using System.Numerics;
using Capsule.Collision.Internal;

namespace Capsule.Collision;

/// <summary>The traversal behind the query seam: the grid walk, the tree walk, and the mover.</summary>
public sealed partial class CollisionWorld2D
{
    // Sweep fractions this close together are one moment: a box landing squarely on a run of tiles
    // meets several faces at once, and every one of them is what stopped it.
    private const float FractionBand = 1e-4f;

    private static void RecordRay(
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count,
        bool all,
        float distance,
        in CollisionTarget target,
        Vector2 normal,
        Vector2 origin,
        Vector2 unit)
    {
        RayHit2D candidate = new(target, origin + (unit * distance), normal, distance);

        if (all)
        {
            Insert(hits, ref count, candidate);

            // A full span is also the limit: both walks stop looking past its farthest entry.
            if (count == hits.Length)
            {
                accumulator.Distance = hits[count - 1].Distance;
            }

            return;
        }

        if (accumulator.Hit
            && !Precedes(candidate, new RayHit2D(accumulator.Target, default, accumulator.Normal, accumulator.Distance)))
        {
            return;
        }

        accumulator.Hit = true;
        accumulator.Distance = distance;
        accumulator.Normal = normal;
        accumulator.Target = target;
    }

    // A full span gives up its farthest entry to a nearer one rather than dropping the newcomer, so
    // which hits survive never depends on the order the traversal met them.
    private static void Insert(Span<RayHit2D> hits, ref int count, in RayHit2D hit)
    {
        if (count == hits.Length)
        {
            if (!Precedes(hit, hits[count - 1]))
            {
                return;
            }

            count--;
        }

        int position = count;
        while (position > 0 && Precedes(hit, hits[position - 1]))
        {
            hits[position] = hits[position - 1];
            position--;
        }

        hits[position] = hit;
        count++;
    }

    // Tiles before colliders, then by slot, then by cell.
    private static bool TargetPrecedes(in CollisionTarget left, in CollisionTarget right)
    {
        if (left.IsGridCell != right.IsGridCell)
        {
            return left.IsGridCell;
        }

        if (left.Collider.Index != right.Collider.Index)
        {
            return left.Collider.Index < right.Collider.Index;
        }

        return left.CellY != right.CellY
            ? left.CellY < right.CellY
            : left.CellX < right.CellX;
    }

    // Nearest first, then a tie-break that reads nothing from the tree's current arrangement.
    private static bool Precedes(in RayHit2D left, in RayHit2D right)
    {
        if (left.Distance != right.Distance)
        {
            return left.Distance < right.Distance;
        }

        return TargetPrecedes(left.Target, right.Target);
    }

    // Returns whether this hit opened a new band, discarding the contacts written before it.
    private static bool Consider(
        ref CastAccumulator accumulator,
        Span<Contact2D> contacts,
        Vector2 translation,
        float fraction,
        in CollisionTarget target,
        Vector2 normal,
        Vector2 point)
    {
        // A surface the sweep is moving away from cannot stop it, which is also what lets a box
        // that starts overlapping something move back out of it.
        if (Vector2.Dot(translation, normal) >= 0f)
        {
            return false;
        }

        bool opened = !accumulator.Hit || fraction < accumulator.Band - FractionBand;

        if (opened)
        {
            accumulator.Hit = true;
            accumulator.Band = fraction;
            accumulator.Fraction = fraction;
            accumulator.Normal = normal;
            accumulator.Point = point;
            accumulator.Target = target;
            accumulator.Count = 0;
        }
        else if (fraction > accumulator.Band + FractionBand)
        {
            return false;
        }
        else if (fraction < accumulator.Fraction
            || (fraction == accumulator.Fraction && TargetPrecedes(target, accumulator.Target)))
        {
            // The band is anchored where it opened; within it the nearest hit is the primary and
            // an exact tie goes to the preceding target, so the primary never widens the band.
            accumulator.Fraction = fraction;
            accumulator.Normal = normal;
            accumulator.Point = point;
            accumulator.Target = target;
        }

        if (accumulator.Count < contacts.Length)
        {
            contacts[accumulator.Count++] = new Contact2D(target, point, normal);
        }

        return opened;
    }

    // The generation is half the answer: an index alone still names the slot after its collider is
    // removed, and the next collider to take it would be suppressed in its place.
    private static bool IsIgnored(ColliderHandle ignore, int index, int generation) =>
        !ignore.IsNone && ignore.Index == index && ignore.Generation == generation;

    private static bool IsBoundaryFace(CellState state, Vector2 normal)
    {
        if (MathF.Abs(normal.X) >= MathF.Abs(normal.Y))
        {
            return (state & (normal.X < 0f ? CellState.FaceMinX : CellState.FaceMaxX)) != 0;
        }

        return (state & (normal.Y < 0f ? CellState.FaceMinY : CellState.FaceMaxY)) != 0;
    }

    // Whether a solid cell's face is a surface this query can meet. The grid culls a face shared
    // with a solid neighbour, which holds only while the query admits every layer the grid uses: a
    // filter that excludes a layer turns those cells into empty space, making a culled face real
    // again. The layer test is reached only for a culled face on a grid the filter partly admits.
    private static bool IsActiveFace(
        GridCollider2D map,
        int x,
        int y,
        CellState state,
        Vector2 normal,
        CollisionFilter filter) =>
        IsBoundaryFace(state, normal)
        || (!map.AdmitsEveryLayer(filter) && !map.NeighbourAdmits(x, y, normal, filter));

    // A face is the degenerate box, which the hull routines read as the segment it is.
    private static Shape2D AsShape(in Aabb2D box) =>
        box.Min.X == box.Max.X || box.Min.Y == box.Max.Y
            ? Shape2D.Segment(box.Min, box.Max)
            : Shape2D.Box(box);

    // The face bits of a cell state, in a fixed order so a cell with several of them is always
    // tested the same way round.
    private static ReadOnlySpan<CellState> Faces =>
        [CellState.FaceMinX, CellState.FaceMaxX, CellState.FaceMinY, CellState.FaceMaxY];

    // How far a shape already reaches past a face's plane, measured inwards. A face is
    // one-directional, so a shape starting more than a slop beyond it is through and meets nothing.
    private static float InwardOf(in Aabb2D bounds, in Aabb2D edge, Vector2 normal) =>
        normal.X != 0f
            ? (normal.X < 0f ? bounds.Max.X - edge.Min.X : edge.Min.X - bounds.Min.X)
            : (normal.Y < 0f ? bounds.Max.Y - edge.Min.Y : edge.Min.Y - bounds.Min.Y);

    private static float SeparationOf(in Shape2D shape, in Shape2D other, out Vector2 normal, out Vector2 point)
    {
        if (shape.Kind == ShapeKind2D.Box && other.Kind == ShapeKind2D.Box)
        {
            return Boxes.Separation(shape.Bounds, other.Bounds, out normal, out point);
        }

        float separation = Gjk.Separation(shape, other, out normal, out point);
        if (normal == Vector2.Zero)
        {
            // The hulls themselves intersect, so the distance carries no direction; the boxes'
            // least-penetration axis is the only one left to answer with.
            Boxes.Separation(shape.Bounds, other.Bounds, out normal, out _);
            point = other.Support(normal) + (normal * other.Radius);
        }

        return separation;
    }

    // A cell is an axis-aligned box even when it is a single face, so a box mover meets terrain
    // through the closed-form sweep and never through the iterated one.
    private static bool SweepAgainstCell(
        in Shape2D moving,
        Vector2 translation,
        in Aabb2D target,
        out float fraction,
        out Vector2 normal,
        out Vector2 point)
    {
        if (moving.Kind != ShapeKind2D.Box)
        {
            return SweepAgainst(moving, translation, AsShape(target), out fraction, out normal, out point);
        }

        if (Boxes.Sweep(moving.Bounds, translation, target, out fraction, out normal))
        {
            point = Vector2.Clamp(moving.Bounds.Center + (translation * fraction), target.Min, target.Max);
            return true;
        }

        point = Vector2.Zero;

        return false;
    }

    private static float SeparationOfCell(in Shape2D shape, in Aabb2D cell, out Vector2 normal, out Vector2 point) =>
        shape.Kind == ShapeKind2D.Box
            ? Boxes.Separation(shape.Bounds, cell, out normal, out point)
            : SeparationOf(shape, AsShape(cell), out normal, out point);

    private static bool SweepAgainst(
        in Shape2D moving,
        Vector2 translation,
        in Shape2D target,
        out float fraction,
        out Vector2 normal,
        out Vector2 point)
    {
        if (moving.Kind == ShapeKind2D.Box && target.Kind == ShapeKind2D.Box)
        {
            if (Boxes.Sweep(moving.Bounds, translation, target.Bounds, out fraction, out normal))
            {
                point = Vector2.Clamp(
                    moving.Bounds.Center + (translation * fraction),
                    target.Bounds.Min,
                    target.Bounds.Max);
                return true;
            }

            point = Vector2.Zero;

            return false;
        }

        if (Gjk.ShapeCast(target, moving, translation, out fraction, out point, out normal))
        {
            return true;
        }

        // Conservative advancement has no time of impact to report out of an existing touch, so a
        // pair already within the skin is decided by whether the sweep drives into it.
        if (SeparationOf(moving, target, out normal, out point) <= ContactSkin
            && Vector2.Dot(translation, normal) < 0f)
        {
            fraction = 0f;
            point = Vector2.Clamp(moving.Bounds.Center, target.Bounds.Min, target.Bounds.Max);
            return true;
        }

        return false;
    }

    // The tree hands proxies back in whatever shape it currently has, so the collider tail of a
    // result set is ordered by handle here and never carries the tree's own arrangement out.
    private static void SortByHandle(Span<Contact2D> contacts) =>
        contacts.Sort(static (left, right) => left.Target.Collider.Index.CompareTo(right.Target.Collider.Index));

    private void RaycastGrids(
        Vector2 origin,
        Vector2 unit,
        CollisionFilter filter,
        ColliderHandle ignore,
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count)
    {
        bool all = !hits.IsEmpty;

        foreach (GridCollider2D map in _grids)
        {
            if (map.Handle == ignore || (filter & map.Layers).IsEmpty)
            {
                continue;
            }

            if (!Segments.RayBoxRange(map.Bounds, origin, unit, accumulator.Distance, out float enter, out float exit, out _))
            {
                continue;
            }

            WalkGrid(map, origin, unit, enter, exit, filter, ref accumulator, hits, ref count, all);
        }
    }

    // Amanatides and Woo: a ray touches only the cells it crosses, in the order it crosses them.
    private void WalkGrid(
        GridCollider2D map,
        Vector2 origin,
        Vector2 unit,
        float enter,
        float exit,
        CollisionFilter filter,
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count,
        bool all)
    {
        int size = map.CellSize;
        Vector2 start = origin + (unit * enter);
        int x = Math.Clamp(GridCollider2D.FloorDiv(start.X, size), 0, map.Width - 1);
        int y = Math.Clamp(GridCollider2D.FloorDiv(start.Y, size), 0, map.Height - 1);

        int stepX = unit.X > 0f ? 1 : (unit.X < 0f ? -1 : 0);
        int stepY = unit.Y > 0f ? 1 : (unit.Y < 0f ? -1 : 0);

        float nextX = (stepX > 0 ? x + 1 : x) * (float)size;
        float nextY = (stepY > 0 ? y + 1 : y) * (float)size;

        float boundaryX = stepX == 0 ? float.PositiveInfinity : enter + ((nextX - start.X) / unit.X);
        float boundaryY = stepY == 0 ? float.PositiveInfinity : enter + ((nextY - start.Y) / unit.Y);
        float strideX = stepX == 0 ? float.PositiveInfinity : size / MathF.Abs(unit.X);
        float strideY = stepY == 0 ? float.PositiveInfinity : size / MathF.Abs(unit.Y);

        while (true)
        {
            if (TestCell(map, x, y, origin, unit, filter, ref accumulator, hits, ref count, all) && !all)
            {
                return;
            }

            // Re-read every step, because the limit tightens as hits are taken: a filled span puts
            // everything beyond its farthest entry out of reach. Strictly greater, so a cell entered
            // exactly at the limit is still tested and the total order decides the tie.
            if (MathF.Min(boundaryX, boundaryY) > MathF.Min(exit, accumulator.Distance))
            {
                return;
            }

            // An exact tie is the ray crossing a cell corner, entering both cells the corner
            // separates at one moment. Stepping X alone would commit to whichever face the box test
            // picks — at a corner, possibly the seam the two share — hiding the exposed face of the
            // cell never visited.
            if (boundaryX == boundaryY
                && (uint)(y + stepY) < (uint)map.Height
                && TestCell(map, x, y + stepY, origin, unit, filter, ref accumulator, hits, ref count, all)
                && !all)
            {
                return;
            }

            if (boundaryX <= boundaryY)
            {
                x += stepX;
                boundaryX += strideX;
            }
            else
            {
                y += stepY;
                boundaryY += strideY;
            }

            if ((uint)x >= (uint)map.Width || (uint)y >= (uint)map.Height)
            {
                return;
            }
        }
    }

    private bool TestCell(
        GridCollider2D map,
        int x,
        int y,
        Vector2 origin,
        Vector2 unit,
        CollisionFilter filter,
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count,
        bool all)
    {
        GridCellsTested++;

        CellState state = map.StateAt(x, y);
        if (state == CellState.None)
        {
            return false;
        }

        CollisionLayer layer = map.LayerOf(x, y);
        if (!filter.Matches(layer))
        {
            return false;
        }

        float limit = accumulator.Distance;
        float t;
        Vector2 normal;

        if ((state & CellState.Solid) != 0)
        {
            if (!Segments.RayBox(map.CellBox(x, y), origin, unit, limit, out t, out normal))
            {
                return false;
            }

            // A zero normal means the ray began inside the cell, where there is no face to test.
            if (normal != Vector2.Zero && !IsActiveFace(map, x, y, state, normal, filter))
            {
                return false;
            }
        }
        else if (!NearestFace(map, x, y, state, origin, unit, limit, out t, out normal))
        {
            return false;
        }

        RecordRay(
            ref accumulator,
            hits,
            ref count,
            all,
            t,
            CollisionTarget.ForGridCell(map.Handle, x, y, layer),
            normal,
            origin,
            unit);

        return true;
    }

    // The first of a partial cell's faces the ray crosses inwards. A face the ray is travelling
    // along or away from is not one it can cross, which is what makes an edge one-directional.
    private static bool NearestFace(
        GridCollider2D map,
        int x,
        int y,
        CellState state,
        Vector2 origin,
        Vector2 unit,
        float limit,
        out float t,
        out Vector2 normal)
    {
        t = 0f;
        normal = Vector2.Zero;
        float nearest = float.PositiveInfinity;

        foreach (CellState face in Faces)
        {
            if ((state & face) == 0)
            {
                continue;
            }

            Vector2 outward = GridCollider2D.FaceNormal(face);
            if (Vector2.Dot(unit, outward) >= 0f)
            {
                continue;
            }

            Aabb2D edge = map.FaceEdge(x, y, face);
            if (Segments.RaySegment(edge.Min, edge.Max, origin, unit, limit, out float faceT) && faceT < nearest)
            {
                nearest = faceT;
                normal = outward;
            }
        }

        if (float.IsPositiveInfinity(nearest))
        {
            return false;
        }

        t = nearest;

        return true;
    }

    private void RaycastColliders(
        Vector2 origin,
        Vector2 unit,
        CollisionFilter filter,
        ColliderHandle ignore,
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count)
    {
        RayVisitor visitor = new(this, origin, unit, filter, ignore, hits, count, accumulator);
        _tree.RayCast(origin, unit, accumulator.Distance, ref visitor);

        count = visitor.Count;
        accumulator = visitor.Accumulator;
    }

    private int Touching(
        in Shape2D world,
        CollisionFilter filter,
        float tolerance,
        ColliderHandle ignore,
        Span<Contact2D> contacts)
    {
        int count = 0;
        Aabb2D probe = world.Bounds.Expanded(tolerance);

        foreach (GridCollider2D map in _grids)
        {
            if (map.Handle == ignore || (filter & map.Layers).IsEmpty || !map.Bounds.Overlaps(probe))
            {
                continue;
            }

            int size = map.CellSize;
            int minX = Math.Max(0, GridCollider2D.FloorDiv(probe.Min.X, size));
            int maxX = Math.Min(map.Width - 1, GridCollider2D.FloorDiv(probe.Max.X, size));
            int minY = Math.Max(0, GridCollider2D.FloorDiv(probe.Min.Y, size));
            int maxY = Math.Min(map.Height - 1, GridCollider2D.FloorDiv(probe.Max.Y, size));

            for (int y = minY; y <= maxY && count < contacts.Length; y++)
            {
                for (int x = minX; x <= maxX && count < contacts.Length; x++)
                {
                    CellState state = map.StateAt(x, y);
                    if (state == CellState.None)
                    {
                        continue;
                    }

                    CollisionLayer layer = map.LayerOf(x, y);
                    if (!filter.Matches(layer))
                    {
                        continue;
                    }

                    if (!TouchingCell(map, x, y, state, world, tolerance, out Vector2 normal, out Vector2 point))
                    {
                        continue;
                    }

                    contacts[count++] = new Contact2D(CollisionTarget.ForGridCell(map.Handle, x, y, layer), point, normal);
                }
            }
        }

        int first = count;
        TouchVisitor visitor = new(this, world, filter, tolerance, ignore, contacts, count);
        _tree.Query(probe, ref visitor);
        count = visitor.Count;

        SortByHandle(contacts[first..count]);

        return count;
    }

    // Whether a shape is within tolerance of a cell, and where. One contact a cell however many
    // faces it carries: the nearest.
    private static bool TouchingCell(
        GridCollider2D map,
        int x,
        int y,
        CellState state,
        in Shape2D world,
        float tolerance,
        out Vector2 normal,
        out Vector2 point)
    {
        if ((state & CellState.Solid) != 0)
        {
            return SeparationOfCell(world, map.CellBox(x, y), out normal, out point) <= tolerance;
        }

        normal = Vector2.Zero;
        point = Vector2.Zero;
        float nearest = float.PositiveInfinity;

        foreach (CellState face in Faces)
        {
            if ((state & face) == 0)
            {
                continue;
            }

            Vector2 outward = GridCollider2D.FaceNormal(face);
            Aabb2D edge = map.FaceEdge(x, y, face);
            float separation = SeparationOfCell(world, edge, out _, out Vector2 facePoint);

            // A face is a surface only to a shape on its outward side; one that has passed through
            // touches it not at all. Which side that is comes from the authored plane, never from
            // the narrowphase, whose least-penetration axis resolves an exact tie towards -X and -Y
            // and would answer differently for a Top than for a Bottom. Measured from the centre of
            // the shape's bounds and inclusive, so a centre exactly on the plane is outward and all
            // four faces read alike.
            if (separation <= tolerance
                && separation < nearest
                && Vector2.Dot(world.Bounds.Center - edge.Min, outward) >= 0f)
            {
                nearest = separation;
                normal = outward;
                point = facePoint;
            }
        }

        return !float.IsPositiveInfinity(nearest);
    }

    private void Cast(
        in Shape2D moving,
        Vector2 translation,
        CollisionFilter filter,
        ColliderHandle ignore,
        Span<Contact2D> contacts,
        ref CastAccumulator accumulator)
    {
        Aabb2D start = moving.Bounds.Expanded(LinearSlop);
        Aabb2D swept = start.Swept(translation);

        // The one derived box both broadphases read; checked here because ShapeCast and every axis
        // of a move arrive through this point.
        RequireFinite(swept, nameof(translation));

        foreach (GridCollider2D map in _grids)
        {
            if (map.Handle == ignore || (filter & map.Layers).IsEmpty || !map.Bounds.Overlaps(swept))
            {
                continue;
            }

            int size = map.CellSize;
            int minX = Math.Max(0, GridCollider2D.FloorDiv(swept.Min.X, size));
            int maxX = Math.Min(map.Width - 1, GridCollider2D.FloorDiv(swept.Max.X, size));

            // Column by column, and within each only the rows the sweep passes through: the band,
            // not the bounding rectangle a long diagonal describes.
            for (int x = minX; x <= maxX; x++)
            {
                if (!ColumnRows(start, translation, x, size, map.Height, out int minY, out int maxY))
                {
                    continue;
                }

                for (int y = minY; y <= maxY; y++)
                {
                    CastCell(map, x, y, moving, translation, filter, contacts, ref accumulator);
                }
            }
        }

        CastVisitor visitor = new(this, moving, translation, filter, ignore, contacts, accumulator);
        _tree.Query(swept, ref visitor);
        accumulator = visitor.Accumulator;

        SortByHandle(contacts[visitor.First..accumulator.Count]);
    }

    // The rows one column shares with the swept shape. The sweep is inside the column's slab over a
    // single interval of the translation, and over that interval the shape's Y range is bounded by
    // its position at the two ends, so the band is exact.
    private static bool ColumnRows(
        in Aabb2D start,
        Vector2 translation,
        int x,
        int size,
        int height,
        out int minY,
        out int maxY)
    {
        minY = 0;
        maxY = -1;

        float low = x * (float)size;
        float high = low + size;
        float enter = 0f;
        float exit = 1f;

        if (translation.X == 0f)
        {
            if (start.Max.X < low || start.Min.X > high)
            {
                return false;
            }
        }
        else
        {
            float first = (low - start.Max.X) / translation.X;
            float second = (high - start.Min.X) / translation.X;
            enter = MathF.Max(0f, MathF.Min(first, second));
            exit = MathF.Min(1f, MathF.Max(first, second));

            if (enter > exit)
            {
                return false;
            }
        }

        bool downwards = translation.Y >= 0f;
        float top = start.Min.Y + (translation.Y * (downwards ? enter : exit));
        float bottom = start.Max.Y + (translation.Y * (downwards ? exit : enter));

        minY = Math.Max(0, GridCollider2D.FloorDiv(top, size));
        maxY = Math.Min(height - 1, GridCollider2D.FloorDiv(bottom, size));

        return minY <= maxY;
    }

    private void CastCell(
        GridCollider2D map,
        int x,
        int y,
        in Shape2D moving,
        Vector2 translation,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ref CastAccumulator accumulator)
    {
        GridCellsTested++;

        CellState state = map.StateAt(x, y);
        if (state == CellState.None)
        {
            return;
        }

        CollisionLayer layer = map.LayerOf(x, y);
        if (!filter.Matches(layer))
        {
            return;
        }

        CollisionTarget target = CollisionTarget.ForGridCell(map.Handle, x, y, layer);

        if ((state & CellState.Solid) != 0)
        {
            if (!SweepAgainstCell(moving, translation, map.CellBox(x, y), out float fraction, out Vector2 normal, out Vector2 point))
            {
                return;
            }

            if (normal != Vector2.Zero && !IsActiveFace(map, x, y, state, normal, filter))
            {
                return;
            }

            Consider(ref accumulator, contacts, translation, fraction, target, normal, point);

            return;
        }

        foreach (CellState face in Faces)
        {
            if ((state & face) == 0)
            {
                continue;
            }

            Vector2 outward = GridCollider2D.FaceNormal(face);
            Aabb2D edge = map.FaceEdge(x, y, face);

            // A face stops only a sweep crossing it inwards that began on its outward side.
            if (Vector2.Dot(translation, outward) >= 0f
                || InwardOf(moving.Bounds, edge, outward) > LinearSlop)
            {
                continue;
            }

            if (!SweepAgainstCell(moving, translation, edge, out float fraction, out Vector2 normal, out Vector2 point)
                || Vector2.Dot(normal, outward) <= 0f)
            {
                continue;
            }

            // The face's own normal, not the narrowphase's: a rounded shape meeting the end of an
            // edge is nearest its endpoint, so GJK answers with the diagonal from that corner — a
            // direction the declared plane does not have.
            Consider(ref accumulator, contacts, translation, fraction, target, outward, point);
        }
    }

    private bool SweepAxis(
        in Shape2D shape,
        ref Vector2 at,
        float delta,
        bool horizontal,
        CollisionFilter filter,
        ColliderHandle ignore,
        Span<Contact2D> contacts,
        ref int written,
        out float moved)
    {
        moved = 0f;
        if (delta == 0f)
        {
            return false;
        }

        Vector2 translation = horizontal ? new Vector2(delta, 0f) : new Vector2(0f, delta);
        Shape2D moving = shape.Translated(at);

        if (moving.Kind == ShapeKind2D.Box)
        {
            // Shrunk on the axis it is not moving along, so a face flush with its side never reads
            // as something in its way and a slide along a flat run cannot catch on a seam. A
            // rounded shape needs no inset: its advance already stops short of a tangent surface.
            Aabb2D bounds = moving.Bounds;
            Vector2 size = bounds.Size;
            float inset = horizontal
                ? MathF.Max(0f, MathF.Min(LinearSlop, (size.Y - (2f * Shape2D.PointTolerance)) * 0.5f))
                : MathF.Max(0f, MathF.Min(LinearSlop, (size.X - (2f * Shape2D.PointTolerance)) * 0.5f));
            Vector2 shrink = horizontal ? new Vector2(0f, inset) : new Vector2(inset, 0f);
            moving = Shape2D.Box(new Aabb2D(bounds.Min + shrink, bounds.Max - shrink));
        }

        CastAccumulator accumulator = default;
        Cast(moving, translation, filter, ignore, contacts[Math.Min(written, contacts.Length)..], ref accumulator);
        written += accumulator.Count;

        if (!accumulator.Hit || accumulator.Fraction >= 1f)
        {
            moved = delta;
            at += translation;
            return false;
        }

        // A slop short of the surface rather than flush, so a rounding error cannot leave the mover
        // a hair inside; the gap is well within the contact skin, so the surface still reports as
        // touched next step.
        float sign = MathF.Sign(delta);
        moved = (delta * accumulator.Fraction) - (sign * LinearSlop);
        if (moved * sign < 0f)
        {
            moved = 0f;
        }

        at += horizontal ? new Vector2(moved, 0f) : new Vector2(0f, moved);

        return true;
    }

    private ref struct RayVisitor : IRayVisitor
    {
        private readonly CollisionWorld2D _world;
        private readonly Vector2 _origin;
        private readonly Vector2 _unit;
        private readonly CollisionFilter _filter;
        private readonly ColliderHandle _ignore;
        private readonly Span<RayHit2D> _hits;

        internal RayVisitor(
            CollisionWorld2D world,
            Vector2 origin,
            Vector2 unit,
            CollisionFilter filter,
            ColliderHandle ignore,
            Span<RayHit2D> hits,
            int count,
            RayAccumulator accumulator)
        {
            _world = world;
            _origin = origin;
            _unit = unit;
            _filter = filter;
            _ignore = ignore;
            _hits = hits;
            Count = count;
            Accumulator = accumulator;
        }

        internal int Count { get; private set; }

        internal RayAccumulator Accumulator { get; private set; }

        public float Visit(int proxyId, float maxFraction)
        {
            int index = _world._tree.UserDataOf(proxyId);
            ref ColliderSlot slot = ref _world._slots[index];

            if (!slot.InUse || slot.Grid is not null || !_filter.Matches(slot.Layer)
                || IsIgnored(_ignore, index, slot.Generation))
            {
                return maxFraction;
            }

            RayAccumulator accumulator = Accumulator;
            if (!Segments.RayShape(slot.World, _origin, _unit, accumulator.Distance, out float t, out Vector2 normal))
            {
                return maxFraction;
            }

            int count = Count;
            RecordRay(
                ref accumulator,
                _hits,
                ref count,
                !_hits.IsEmpty,
                t,
                CollisionTarget.ForCollider(_world.HandleAt(index), slot.Layer),
                normal,
                _origin,
                _unit);

            Count = count;
            Accumulator = accumulator;

            return accumulator.Distance;
        }
    }

    private ref struct TouchVisitor : ITreeVisitor
    {
        private readonly CollisionWorld2D _world;
        private readonly Shape2D _shape;
        private readonly CollisionFilter _filter;
        private readonly float _tolerance;
        private readonly ColliderHandle _ignore;
        private readonly Span<Contact2D> _contacts;

        internal TouchVisitor(
            CollisionWorld2D world,
            in Shape2D shape,
            CollisionFilter filter,
            float tolerance,
            ColliderHandle ignore,
            Span<Contact2D> contacts,
            int count)
        {
            _world = world;
            _shape = shape;
            _filter = filter;
            _tolerance = tolerance;
            _ignore = ignore;
            _contacts = contacts;
            Count = count;
        }

        internal int Count { get; private set; }

        public bool Visit(int proxyId)
        {
            if (Count == _contacts.Length)
            {
                return false;
            }

            int index = _world._tree.UserDataOf(proxyId);
            ref ColliderSlot slot = ref _world._slots[index];

            if (!slot.InUse || slot.Grid is not null || !_filter.Matches(slot.Layer)
                || IsIgnored(_ignore, index, slot.Generation))
            {
                return true;
            }

            if (SeparationOf(_shape, slot.World, out Vector2 normal, out Vector2 point) > _tolerance)
            {
                return true;
            }

            _contacts[Count++] = new Contact2D(
                CollisionTarget.ForCollider(_world.HandleAt(index), slot.Layer),
                point,
                normal);

            return true;
        }
    }

    private ref struct CastVisitor : ITreeVisitor
    {
        private readonly CollisionWorld2D _world;
        private readonly Shape2D _moving;
        private readonly Vector2 _translation;
        private readonly CollisionFilter _filter;
        private readonly ColliderHandle _ignore;
        private readonly Span<Contact2D> _contacts;

        internal CastVisitor(
            CollisionWorld2D world,
            in Shape2D moving,
            Vector2 translation,
            CollisionFilter filter,
            ColliderHandle ignore,
            Span<Contact2D> contacts,
            CastAccumulator accumulator)
        {
            _world = world;
            _moving = moving;
            _translation = translation;
            _filter = filter;
            _ignore = ignore;
            _contacts = contacts;
            Accumulator = accumulator;
            First = accumulator.Count;
        }

        internal CastAccumulator Accumulator { get; private set; }

        /// <summary>Where the tree-phase contacts start; the grid phase's stay in traversal order.</summary>
        internal int First { get; private set; }

        public bool Visit(int proxyId)
        {
            int index = _world._tree.UserDataOf(proxyId);
            ref ColliderSlot slot = ref _world._slots[index];

            if (!slot.InUse || slot.Grid is not null || !_filter.Matches(slot.Layer)
                || IsIgnored(_ignore, index, slot.Generation))
            {
                return true;
            }

            if (!SweepAgainst(_moving, _translation, slot.World, out float fraction, out Vector2 normal, out Vector2 point))
            {
                return true;
            }

            CastAccumulator accumulator = Accumulator;
            bool opened = Consider(
                ref accumulator,
                _contacts,
                _translation,
                fraction,
                CollisionTarget.ForCollider(_world.HandleAt(index), slot.Layer),
                normal,
                point);
            Accumulator = accumulator;

            if (opened)
            {
                // The band that opened discarded every contact before it, grid cells included.
                First = 0;
            }

            return true;
        }
    }
}
