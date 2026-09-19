using System.Numerics;
using Capsule.Physics.Internal;

namespace Capsule.Physics;

// The traversal behind the query seam, covering the grid walk, the tree walk and the mover.
public sealed partial class CollisionWorld2D
{
    // Sweep fractions this close together count as one moment. A box landing squarely on a run of tiles
    // meets several faces at once, and all of them stopped it.
    private const float FractionBand = 1e-4f;

    // The face bits of a cell state, in a fixed order. A cell with several faces is tested the same way
    // round every time.
    private static ReadOnlySpan<CellState2D> Faces =>
        [CellState2D.FaceMinX, CellState2D.FaceMaxX, CellState2D.FaceMinY, CellState2D.FaceMaxY];

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

            // A full span is also the limit. Both walks stop looking past its farthest entry.
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

    // A full span gives up its farthest entry to a nearer hit, so which hits survive does not depend on
    // the order the traversal met them.
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

    // Writes a contact into the run starting at first, keeping that run ordered by collider handle. A
    // full span gives up its last entry to a lower handle, so which contacts a short span keeps does not
    // depend on broadphase order.
    private static void InsertByHandle(Span<Contact2D> contacts, int first, ref int written, in Contact2D contact)
    {
        int handle = contact.Target.Collider.Index;

        if (written == contacts.Length)
        {
            if (first == written || contacts[written - 1].Target.Collider.Index <= handle)
            {
                return;
            }

            written--;
        }

        int position = written;
        while (position > first && contacts[position - 1].Target.Collider.Index > handle)
        {
            contacts[position] = contacts[position - 1];
            position--;
        }

        contacts[position] = contact;
        written++;
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

    // Nearest first, with a tie-break that does not read the tree's current arrangement.
    private static bool Precedes(in RayHit2D left, in RayHit2D right)
    {
        if (left.Distance != right.Distance)
        {
            return left.Distance < right.Distance;
        }

        return TargetPrecedes(left.Target, right.Target);
    }

    // Records a sweep hit, keeping only the nearest band of them. Returns whether this hit opened a new
    // band, which discards the contacts written before it.
    private static bool RecordCast(
        ref CastAccumulator accumulator,
        Span<Contact2D> contacts,
        Vector2 translation,
        float fraction,
        in CollisionTarget target,
        Vector2 normal,
        Vector2 point,
        bool byHandle)
    {
        // A surface the sweep is moving away from cannot stop it, which lets a box that starts
        // overlapping something move back out.
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
            accumulator.Found = 0;
            accumulator.Written = 0;
            accumulator.First = 0;
        }
        else if (fraction > accumulator.Band + FractionBand)
        {
            return false;
        }
        else if (fraction < accumulator.Fraction
            || (fraction == accumulator.Fraction && TargetPrecedes(target, accumulator.Target)))
        {
            // The band stays anchored where it opened. Inside it the nearest hit is the primary, and a
            // tie goes to the preceding target.
            accumulator.Fraction = fraction;
            accumulator.Normal = normal;
            accumulator.Point = point;
            accumulator.Target = target;
        }

        accumulator.Found++;
        Contact2D contact = new(target, point, normal);

        if (byHandle)
        {
            InsertByHandle(contacts, accumulator.First, ref accumulator.Written, contact);
        }
        else if (accumulator.Written < contacts.Length)
        {
            contacts[accumulator.Written++] = contact;
        }

        return opened;
    }

    // The generation matters because an index alone still names the slot after its collider is removed,
    // and the next collider in that slot would be suppressed in its place.
    private static bool IsIgnored(ColliderHandle ignore, int index, int generation) =>
        !ignore.IsNone && ignore.Index == index && ignore.Generation == generation;

    // Whether a solid cell's face is a surface this query can meet. The grid culls a face shared with a
    // solid neighbour, and a filter that excludes that neighbour's layer turns it into empty space,
    // which makes the culled face real again. A partly admitted grid re-decides the culling here.
    private static bool IsActiveFace(
        GridCollider2D grid,
        int x,
        int y,
        CellState2D state,
        Vector2 normal,
        CollisionFilter filter,
        bool admitsEvery)
    {
        CellState2D face = GridCollider2D.FaceOf(normal);

        return (state & face) != 0 || (!admitsEvery && !grid.NeighbourAdmits(x, y, face, filter));
    }

    // How far a shape already reaches past a face's plane, measured inwards. A face is one-directional.
    // A shape starting more than a slop beyond it has passed through and meets nothing.
    private static float DepthPastFace(in Aabb2D bounds, in Aabb2D edge, Vector2 normal) =>
        normal.X != 0f
            ? (normal.X < 0f ? bounds.Max.X - edge.Min.X : edge.Min.X - bounds.Min.X)
            : (normal.Y < 0f ? bounds.Max.Y - edge.Min.Y : edge.Min.Y - bounds.Min.Y);

    // How far apart two shapes are, negative when they overlap, with the surface point and the normal
    // on the second. A grid cell and one of its faces are axis-aligned like a box, so both take the
    // closed form against a box mover.
    private static float Separation(in Shape2D shape, in Shape2D other, out Vector2 normal, out Vector2 point)
    {
        if (shape.Kind == ShapeKind2D.Box && other.Kind is ShapeKind2D.Box or ShapeKind2D.Segment)
        {
            return Boxes2D.Separation(shape.Bounds, other.Bounds, out normal, out point);
        }

        float separation = Gjk2D.Separation(shape, other, out normal, out point);
        if (normal == Vector2.Zero)
        {
            // The hulls are too close to carry a direction, so the boxes' least-penetration axis answers
            // instead.
            Boxes2D.Separation(shape.Bounds, other.Bounds, out normal, out _);
            point = other.Support(normal) + (normal * other.Radius);
        }

        return separation;
    }

    // The same against a grid cell or one of its faces, which stays an Aabb2D for a box mover. Only the
    // iterated routines read the hull a Shape2D carries, and building one per cell costs a tenth of a
    // query step.
    private static float Separation(in Shape2D shape, in Aabb2D cell, out Vector2 normal, out Vector2 point) =>
        shape.Kind == ShapeKind2D.Box
            ? Boxes2D.Separation(shape.Bounds, cell, out normal, out point)
            : Separation(shape, Shape2D.OfCell(cell), out normal, out point);

    // The fraction of translation at which moving first touches target, with the contact point and
    // normal there. Conservative advancement has no time of impact to report out of an existing touch.
    // A pair already within the skin is decided by whether the sweep drives into it.
    private static bool Sweep(
        in Shape2D moving,
        Vector2 translation,
        in Shape2D target,
        out float fraction,
        out Vector2 normal,
        out Vector2 point)
    {
        if (moving.Kind == ShapeKind2D.Box && target.Kind is ShapeKind2D.Box or ShapeKind2D.Segment)
        {
            return SweepBoxes(moving.Bounds, translation, target.Bounds, out fraction, out normal, out point);
        }

        if (Gjk2D.ShapeCast(target, moving, translation, out fraction, out point, out normal))
        {
            return true;
        }

        if (Separation(moving, target, out normal, out point) <= CollisionTolerance.ContactSkin
            && Vector2.Dot(translation, normal) < 0f)
        {
            fraction = 0f;
            point = Vector2.Clamp(moving.Bounds.Center, target.Bounds.Min, target.Bounds.Max);

            return true;
        }

        return false;
    }

    // The same against a grid cell or one of its faces, which a box mover meets without a Shape2D.
    private static bool Sweep(
        in Shape2D moving,
        Vector2 translation,
        in Aabb2D cell,
        out float fraction,
        out Vector2 normal,
        out Vector2 point) =>
        moving.Kind == ShapeKind2D.Box
            ? SweepBoxes(moving.Bounds, translation, cell, out fraction, out normal, out point)
            : Sweep(moving, translation, Shape2D.OfCell(cell), out fraction, out normal, out point);

    // The closed-form box sweep, with the witness point from the clamp.
    private static bool SweepBoxes(
        in Aabb2D moving,
        Vector2 translation,
        in Aabb2D target,
        out float fraction,
        out Vector2 normal,
        out Vector2 point)
    {
        if (Boxes2D.Sweep(moving, translation, target, out fraction, out normal))
        {
            point = Vector2.Clamp(moving.Center + (translation * fraction), target.Min, target.Max);

            return true;
        }

        point = Vector2.Zero;

        return false;
    }

    // The preamble every grid walk shares. Reports whether this query collides with the cell at (x, y),
    // and what it collides as.
    private bool TryCell(
        GridCollider2D grid,
        int x,
        int y,
        CollisionFilter filter,
        out CellState2D state,
        out CollisionLayer layer)
    {
        GridCellsTested++;
        layer = default;
        state = grid.StateAt(x, y);

        if (state == CellState2D.None)
        {
            return false;
        }

        layer = grid.LayerOf(x, y);

        return filter.Admits(layer);
    }

    // The preamble every proxy visit shares. Reports whether the proxy names a live shape collider this
    // query may hit, and which slot holds it.
    private bool TryProxy(int proxyId, CollisionFilter filter, ColliderHandle ignore, out int index)
    {
        index = _tree.UserDataOf(proxyId);
        ref ColliderSlot slot = ref _slots[index];

        return slot.InUse
            && slot.Grid is null
            && filter.Admits(slot.Layer)
            && !IsIgnored(ignore, index, slot.Generation);
    }

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

        foreach (GridCollider2D grid in Grids)
        {
            if (grid.Handle == ignore || (filter & grid.Layers).IsEmpty)
            {
                continue;
            }

            if (!Rays2D.RayBoxRange(grid.Bounds, origin, unit, accumulator.Distance, out float enter, out float exit, out _))
            {
                continue;
            }

            WalkGrid(grid, origin, unit, enter, exit, filter, grid.AdmitsEveryLayer(filter), ref accumulator, hits, ref count, all);
        }
    }

    // Amanatides and Woo. A ray touches only the cells it crosses, in the order it crosses them.
    private void WalkGrid(
        GridCollider2D grid,
        Vector2 origin,
        Vector2 unit,
        float enter,
        float exit,
        CollisionFilter filter,
        bool admitsEvery,
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count,
        bool all)
    {
        int size = grid.CellSize;
        Vector2 start = origin + (unit * enter);
        int x = Math.Clamp(GridCollider2D.FloorDiv(start.X, size), 0, grid.Width - 1);
        int y = Math.Clamp(GridCollider2D.FloorDiv(start.Y, size), 0, grid.Height - 1);

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
            if (TestCell(grid, x, y, origin, unit, filter, admitsEvery, ref accumulator, hits, ref count, all) && !all)
            {
                return;
            }

            // Re-read every step, because the limit tightens as hits are taken and a filled span puts
            // everything beyond its farthest entry out of reach. The comparison is strictly greater, so
            // a cell entered right at the limit is still tested and the total order decides the tie.
            if (MathF.Min(boundaryX, boundaryY) > MathF.Min(exit, accumulator.Distance))
            {
                return;
            }

            // A tie means the ray crosses a cell corner and enters both cells at once. Stepping X alone
            // would commit to whichever face the box test picks, perhaps the seam the two cells share,
            // and hide the exposed face of the cell never visited.
            if (boundaryX == boundaryY
                && (uint)(y + stepY) < (uint)grid.Height
                && TestCell(grid, x, y + stepY, origin, unit, filter, admitsEvery, ref accumulator, hits, ref count, all)
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

            if ((uint)x >= (uint)grid.Width || (uint)y >= (uint)grid.Height)
            {
                return;
            }
        }
    }

    private bool TestCell(
        GridCollider2D grid,
        int x,
        int y,
        Vector2 origin,
        Vector2 unit,
        CollisionFilter filter,
        bool admitsEvery,
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count,
        bool all)
    {
        if (!TryCell(grid, x, y, filter, out CellState2D state, out CollisionLayer layer))
        {
            return false;
        }

        float limit = accumulator.Distance;
        float t;
        Vector2 normal;

        if ((state & CellState2D.Solid) != 0)
        {
            Aabb2D box = grid.CellBox(x, y);
            if (!Rays2D.RayBox(box, origin, unit, limit, out t, out normal))
            {
                return false;
            }

            if (normal == Vector2.Zero)
            {
                // The ray began inside the cell, where there is no face to test, so the hit names the
                // nearest side.
                normal = Rays2D.NearestFace(box, origin);
            }
            else if (!IsActiveFace(grid, x, y, state, normal, filter, admitsEvery))
            {
                return false;
            }
        }
        else if (!FirstFaceCrossed(grid, x, y, state, origin, unit, limit, out t, out normal))
        {
            return false;
        }

        RecordRay(
            ref accumulator,
            hits,
            ref count,
            all,
            t,
            CollisionTarget.ForGridCell(grid.Handle, x, y, layer),
            normal,
            origin,
            unit);

        return true;
    }

    // The first of a partial cell's faces the ray crosses inwards. A ray travelling along a face or away
    // from it cannot cross it, which makes an edge one-directional.
    private static bool FirstFaceCrossed(
        GridCollider2D grid,
        int x,
        int y,
        CellState2D state,
        Vector2 origin,
        Vector2 unit,
        float limit,
        out float t,
        out Vector2 normal)
    {
        t = 0f;
        normal = Vector2.Zero;
        float nearest = float.PositiveInfinity;

        foreach (CellState2D face in Faces)
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

            Aabb2D edge = grid.FaceEdge(x, y, face);
            if (Rays2D.RaySegment(edge.Min, edge.Max, origin, unit, limit, out float faceT) && faceT < nearest)
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
        _tree.RayCast(origin, unit, accumulator.Distance, filter.Bits, ref visitor);

        count = visitor.Count;
        accumulator = visitor.Accumulator;
    }

    private int FindContacts(
        in Shape2D world,
        CollisionFilter filter,
        float tolerance,
        ColliderHandle ignore,
        Span<Contact2D> contacts)
    {
        int found = 0;
        int written = 0;
        Aabb2D probe = world.Bounds.Expanded(tolerance);

        foreach (GridCollider2D grid in Grids)
        {
            if (grid.Handle == ignore || (filter & grid.Layers).IsEmpty || !grid.Bounds.Overlaps(probe))
            {
                continue;
            }

            int size = grid.CellSize;
            int minX = Math.Max(0, GridCollider2D.FloorDiv(probe.Min.X, size));
            int maxX = Math.Min(grid.Width - 1, GridCollider2D.FloorDiv(probe.Max.X, size));
            int minY = Math.Max(0, GridCollider2D.FloorDiv(probe.Min.Y, size));
            int maxY = Math.Min(grid.Height - 1, GridCollider2D.FloorDiv(probe.Max.Y, size));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!TryCell(grid, x, y, filter, out CellState2D state, out CollisionLayer layer)
                        || !CellContact(grid, x, y, state, world, tolerance, out Vector2 normal, out Vector2 point))
                    {
                        continue;
                    }

                    found++;
                    if (written < contacts.Length)
                    {
                        contacts[written++] = new Contact2D(
                            CollisionTarget.ForGridCell(grid.Handle, x, y, layer),
                            point,
                            normal);
                    }
                }
            }
        }

        TouchVisitor visitor = new(this, world, filter, tolerance, ignore, contacts, written, found);
        _tree.Query(probe, filter.Bits, ref visitor);

        return visitor.Found;
    }

    // Whether a shape is within tolerance of a cell, and where. A cell reports one contact, the nearest,
    // however many faces it carries.
    private static bool CellContact(
        GridCollider2D grid,
        int x,
        int y,
        CellState2D state,
        in Shape2D world,
        float tolerance,
        out Vector2 normal,
        out Vector2 point)
    {
        if ((state & CellState2D.Solid) != 0)
        {
            return Separation(world, grid.CellBox(x, y), out normal, out point) <= tolerance;
        }

        normal = Vector2.Zero;
        point = Vector2.Zero;
        float nearest = float.PositiveInfinity;

        foreach (CellState2D face in Faces)
        {
            if ((state & face) == 0)
            {
                continue;
            }

            Vector2 outward = GridCollider2D.FaceNormal(face);
            Aabb2D edge = grid.FaceEdge(x, y, face);
            float separation = Separation(world, edge, out _, out Vector2 facePoint);

            // A face is a surface only to a shape on its outward side. The authored plane decides that
            // side, not the narrowphase, whose least-penetration axis resolves a tie towards -X and -Y
            // and would answer differently for a Top than for a Bottom. The test is inclusive, so a
            // centre on the plane counts as outward and all four faces read alike.
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
        Aabb2D start = moving.Bounds.Expanded(CollisionTolerance.LinearSlop);
        Aabb2D swept = start.Swept(translation);

        foreach (GridCollider2D grid in Grids)
        {
            if (grid.Handle == ignore || (filter & grid.Layers).IsEmpty || !grid.Bounds.Overlaps(swept))
            {
                continue;
            }

            bool admitsEvery = grid.AdmitsEveryLayer(filter);
            int size = grid.CellSize;
            int minX = Math.Max(0, GridCollider2D.FloorDiv(swept.Min.X, size));
            int maxX = Math.Min(grid.Width - 1, GridCollider2D.FloorDiv(swept.Max.X, size));

            // Column by column, and within each only the rows the sweep passes through. That band is
            // narrower than the bounding rectangle of a long diagonal.
            for (int x = minX; x <= maxX; x++)
            {
                if (!ColumnRows(start, translation, x, size, grid.Height, out int minY, out int maxY))
                {
                    continue;
                }

                for (int y = minY; y <= maxY; y++)
                {
                    CastCell(grid, x, y, moving, translation, filter, admitsEvery, contacts, ref accumulator);
                }
            }
        }

        // The grid phase's contacts stay in traversal order, so the collider run starts after them.
        accumulator.First = accumulator.Written;

        CastVisitor visitor = new(this, moving, translation, filter, ignore, contacts, accumulator);
        _tree.Query(swept, filter.Bits, ref visitor);
        accumulator = visitor.Accumulator;
    }

    // The rows one column shares with the swept shape. The sweep is inside the column's slab over a
    // single interval of the translation, and over that interval the shape's Y range is bounded by its
    // position at the two ends, so the band is exact.
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
        GridCollider2D grid,
        int x,
        int y,
        in Shape2D moving,
        Vector2 translation,
        CollisionFilter filter,
        bool admitsEvery,
        Span<Contact2D> contacts,
        ref CastAccumulator accumulator)
    {
        if (!TryCell(grid, x, y, filter, out CellState2D state, out CollisionLayer layer))
        {
            return;
        }

        CollisionTarget target = CollisionTarget.ForGridCell(grid.Handle, x, y, layer);

        if ((state & CellState2D.Solid) != 0)
        {
            if (!Sweep(moving, translation, grid.CellBox(x, y), out float fraction, out Vector2 normal, out Vector2 point))
            {
                return;
            }

            if (normal != Vector2.Zero && !IsActiveFace(grid, x, y, state, normal, filter, admitsEvery))
            {
                return;
            }

            RecordCast(ref accumulator, contacts, translation, fraction, target, normal, point, false);

            return;
        }

        foreach (CellState2D face in Faces)
        {
            if ((state & face) == 0)
            {
                continue;
            }

            Vector2 outward = GridCollider2D.FaceNormal(face);
            Aabb2D edge = grid.FaceEdge(x, y, face);

            // A face stops only a sweep crossing it inwards that began on its outward side.
            if (Vector2.Dot(translation, outward) >= 0f
                || DepthPastFace(moving.Bounds, edge, outward) > CollisionTolerance.LinearSlop)
            {
                continue;
            }

            if (!Sweep(moving, translation, edge, out float fraction, out Vector2 normal, out Vector2 point)
                || Vector2.Dot(normal, outward) <= 0f)
            {
                continue;
            }

            // Report the face's own normal. A rounded shape meeting the end of an edge is nearest its
            // endpoint, where GJK answers with a diagonal the declared plane does not have.
            RecordCast(ref accumulator, contacts, translation, fraction, target, outward, point, false);
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
        ref int found,
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
            // Shrunk on the axis it is not moving along. A face flush with its side then does not read
            // as an obstacle, and a slide along a flat run cannot catch on a seam. A rounded shape needs
            // no inset, since its advance already stops short of a tangent surface.
            Aabb2D bounds = moving.Bounds;
            Vector2 size = bounds.Size;
            float inset = horizontal
                ? MathF.Max(0f, MathF.Min(CollisionTolerance.LinearSlop, (size.Y - (2f * Shape2D.PointTolerance)) * 0.5f))
                : MathF.Max(0f, MathF.Min(CollisionTolerance.LinearSlop, (size.X - (2f * Shape2D.PointTolerance)) * 0.5f));
            Vector2 shrink = horizontal ? new Vector2(0f, inset) : new Vector2(inset, 0f);
            moving = Shape2D.Box(new Aabb2D(bounds.Min + shrink, bounds.Max - shrink));
        }

        CastAccumulator accumulator = default;
        Cast(moving, translation, filter, ignore, contacts[Math.Min(written, contacts.Length)..], ref accumulator);
        written += accumulator.Written;
        found += accumulator.Found;

        if (!accumulator.Hit || accumulator.Fraction >= 1f)
        {
            moved = delta;
            at += translation;
            return false;
        }

        // Stop a slop short of the surface. Rounding error then cannot leave the mover inside it, and the
        // gap is within the contact skin, so the surface still reports as touched.
        float sign = MathF.Sign(delta);
        moved = (delta * accumulator.Fraction) - (sign * CollisionTolerance.LinearSlop);
        if (moved * sign < 0f)
        {
            moved = 0f;
        }

        at += horizontal ? new Vector2(moved, 0f) : new Vector2(0f, moved);

        return true;
    }

    private ref struct RayVisitor : IRayVisitor2D
    {
        private readonly CollisionWorld2D _world;
        private readonly Vector2 _origin;
        private readonly Vector2 _unit;
        private readonly CollisionFilter _filter;
        private readonly ColliderHandle _ignore;
        private readonly Span<RayHit2D> _hits;

        // These are fields because the visitor is handed to the tree by reference and every visited proxy
        // reads and writes them in place.
        internal int Count;
        internal RayAccumulator Accumulator;

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

        public float Visit(int proxyId, float maxFraction)
        {
            if (!_world.TryProxy(proxyId, _filter, _ignore, out int index))
            {
                return maxFraction;
            }

            ref ColliderSlot slot = ref _world._slots[index];

            if (!Rays2D.RayShape(slot.World, _origin, _unit, Accumulator.Distance, out float t, out Vector2 normal))
            {
                return maxFraction;
            }

            RecordRay(
                ref Accumulator,
                _hits,
                ref Count,
                !_hits.IsEmpty,
                t,
                CollisionTarget.ForCollider(_world.HandleAt(index), slot.Layer),
                normal,
                _origin,
                _unit);

            return Accumulator.Distance;
        }
    }

    private ref struct TouchVisitor : ITreeVisitor2D
    {
        private readonly CollisionWorld2D _world;
        private readonly Shape2D _shape;
        private readonly CollisionFilter _filter;
        private readonly float _tolerance;
        private readonly ColliderHandle _ignore;
        private readonly Span<Contact2D> _contacts;
        private readonly int _first;

        private int _written;

        // How many overlaps there are, span or no span.
        internal int Found;

        internal TouchVisitor(
            CollisionWorld2D world,
            in Shape2D shape,
            CollisionFilter filter,
            float tolerance,
            ColliderHandle ignore,
            Span<Contact2D> contacts,
            int written,
            int found)
        {
            _world = world;
            _shape = shape;
            _filter = filter;
            _tolerance = tolerance;
            _ignore = ignore;
            _contacts = contacts;
            _first = written;
            _written = written;
            Found = found;
        }

        public bool Visit(int proxyId)
        {
            if (!_world.TryProxy(proxyId, _filter, _ignore, out int index))
            {
                return true;
            }

            ref ColliderSlot slot = ref _world._slots[index];

            if (Separation(_shape, slot.World, out Vector2 normal, out Vector2 point) > _tolerance)
            {
                return true;
            }

            Found++;
            InsertByHandle(
                _contacts,
                _first,
                ref _written,
                new Contact2D(CollisionTarget.ForCollider(_world.HandleAt(index), slot.Layer), point, normal));

            return true;
        }
    }

    private ref struct CastVisitor : ITreeVisitor2D
    {
        private readonly CollisionWorld2D _world;
        private readonly Shape2D _moving;
        private readonly Vector2 _translation;
        private readonly CollisionFilter _filter;
        private readonly ColliderHandle _ignore;
        private readonly Span<Contact2D> _contacts;

        internal CastAccumulator Accumulator;

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
        }

        public bool Visit(int proxyId)
        {
            if (!_world.TryProxy(proxyId, _filter, _ignore, out int index))
            {
                return true;
            }

            ref ColliderSlot slot = ref _world._slots[index];

            if (!Sweep(_moving, _translation, slot.World, out float fraction, out Vector2 normal, out Vector2 point))
            {
                return true;
            }

            RecordCast(
                ref Accumulator,
                _contacts,
                _translation,
                fraction,
                CollisionTarget.ForCollider(_world.HandleAt(index), slot.Layer),
                normal,
                point,
                true);

            return true;
        }
    }
}
