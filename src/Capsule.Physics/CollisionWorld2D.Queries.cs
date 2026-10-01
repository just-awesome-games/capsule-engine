using System.Numerics;
using Capsule.Physics.Internal;

namespace Capsule.Physics;

// The traversal behind the query seam, covering the grid walk, the tree walk and the mover.
public sealed partial class CollisionWorld2D
{
    // Sweep fractions this close together count as one moment. A box landing squarely on a run of tiles
    // meets several faces at once, and all of them stopped it.
    private const float FractionBand = 1e-4f;

    // How far a translation must lean into a surface, as a share of its length, to be driven into it.
    // A slide projected along a surface keeps a rounding residue far below this, so the surface it
    // slides along never stops it again.
    private const float Along = 1e-4f;

    private static void RecordRay(
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count,
        float distance,
        in CollisionTarget target,
        Vector2 normal,
        Vector2 origin,
        Vector2 unit)
    {
        RayHit2D candidate = new(target, origin + (unit * distance), normal, distance);

        if (!hits.IsEmpty)
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

    // Records a sweep hit. The band is every hit within FractionBand of the nearest, and the nearest
    // is its primary, with a tie going to the preceding target. Neither depends on the order the
    // traversal meets the hits in.
    private void RecordCast(
        ref CastAccumulator accumulator,
        Span<Contact2D> contacts,
        Vector2 translation,
        float fraction,
        in CollisionTarget target,
        Vector2 normal,
        Vector2 point)
    {
        // A surface the sweep is moving away from or along cannot stop it. A box that starts
        // overlapping something can move back out.
        if (Vector2.Dot(translation, normal) >= accumulator.Lean)
        {
            return;
        }

        // The second walk of an overflowing band knows its primary, and writes each band hit as it
        // arrives.
        if (accumulator.Anchored)
        {
            if (fraction <= accumulator.Fraction + FractionBand)
            {
                Emit(ref accumulator, contacts, new Contact2D(target, point, normal, 0f));
            }

            return;
        }

        if (!accumulator.Hit
            || fraction < accumulator.Fraction
            || (fraction == accumulator.Fraction && TargetPrecedes(target, accumulator.Target)))
        {
            if (accumulator.Hit && fraction < accumulator.Fraction)
            {
                DropBandPast(fraction + FractionBand);
            }

            accumulator.Hit = true;
            accumulator.Fraction = fraction;
            accumulator.Normal = normal;
            accumulator.Point = point;
            accumulator.Target = target;
        }
        else if (fraction > accumulator.Fraction + FractionBand)
        {
            return;
        }

        if (_bandCount == _band.Length)
        {
            accumulator.Overflowed = true;
            return;
        }

        _band[_bandCount++] = new BandHit(fraction, new Contact2D(target, point, normal, 0f));
    }

    // Keeps the band's hits at or before `limit`, in the order they arrived.
    private void DropBandPast(float limit)
    {
        int kept = 0;
        for (int index = 0; index < _bandCount; index++)
        {
            if (_band[index].Fraction <= limit)
            {
                _band[kept++] = _band[index];
            }
        }

        _bandCount = kept;
    }

    // Writes the band into the caller's span.
    private void WriteBand(ref CastAccumulator accumulator, Span<Contact2D> contacts)
    {
        for (int index = 0; index < _bandCount; index++)
        {
            Emit(ref accumulator, contacts, _band[index].Contact);
        }
    }

    // Counts one band hit and writes it if the span has room. Grid cells come first in the grids' own
    // traversal order, and colliders follow by handle. A short span keeps the lowest handles.
    private static void Emit(ref CastAccumulator accumulator, Span<Contact2D> contacts, in Contact2D contact)
    {
        accumulator.Found++;
        if (contact.Target.IsGridCell)
        {
            if (accumulator.Written < contacts.Length)
            {
                contacts[accumulator.Written++] = contact;
            }

            accumulator.FirstCollider = accumulator.Written;
        }
        else
        {
            InsertByHandle(contacts, accumulator.FirstCollider, ref accumulator.Written, contact);
        }
    }

    // The generation matters because an index alone still names the slot after its collider is removed,
    // and the next collider in that slot would be suppressed in its place.
    private static bool IsIgnored(ColliderHandle ignore, int index, int generation) =>
        !ignore.IsNone && ignore.Index == index && ignore.Generation == generation;

    // Whether a solid cell's face is a surface this query can meet. The grid culls a face shared with a
    // solid neighbour, and a filter that excludes that neighbour's layer turns it into empty space,
    // which makes the culled face real again. A partly admitted grid re-decides the culling here.
    private static bool IsActiveFace(
        CollisionGrid2D grid,
        int x,
        int y,
        CellState2D state,
        Vector2 normal,
        CollisionFilter filter,
        bool admitsEvery)
    {
        CellState2D face = CollisionGrid2D.FaceOf(normal);

        return (state & face) != 0 || (!admitsEvery && !grid.NeighbourAdmits(x, y, face, filter));
    }

    // How far a shape already reaches past the line through `point` along `normal`, measured inwards.
    // An edge is one-sided. A shape starting more than a slop beyond it has passed through and meets
    // nothing.
    private static float DepthPast(in Shape2D shape, Vector2 point, Vector2 normal) =>
        Vector2.Dot(point - shape.Support(-normal), normal) + shape.Radius;

    // The normal a sweep meets an edge along. A box face meeting the end of an edge rests on that
    // endpoint, and its face's axis is the surface there, not the edge's slope. The narrowphase normal
    // carries rounding error, so it only chooses between the two exact candidates.
    private static Vector2 EdgeNormal(in Shape2D moving, Vector2 swept, Vector2 outward)
    {
        if (moving.Kind != ShapeKind2D.Box)
        {
            return outward;
        }

        Vector2 axis = MathF.Abs(swept.X) >= MathF.Abs(swept.Y)
            ? new Vector2(MathF.Sign(swept.X), 0f)
            : new Vector2(0f, MathF.Sign(swept.Y));

        return Vector2.Dot(swept, axis) > Vector2.Dot(swept, outward) && Vector2.Dot(axis, outward) > 0f
            ? axis
            : outward;
    }

    // Whether a mover starts across an edge rather than on its outward side. Reaching past the edge's
    // line is not enough. A box beside the top corner of a step reaches past the line of the step's
    // slope while still standing clear of the slope itself, and that corner must stop it.
    private static bool StartedPast(in Shape2D moving, Vector2 start, Vector2 end, Vector2 outward) =>
        DepthPast(moving, start, outward) > CollisionTolerance.LinearSlop
        && Separation(moving, Shape2D.Segment(start, end), out _, out _) <= 0f;

    // How far a mover reaches past the far side of a one-way collider along its surface normal. A
    // mover more than a slop past has started inside it or beyond it, and passes through.
    private static float DepthPast(in Shape2D moving, in Shape2D target, Vector2 normal) =>
        Vector2.Dot(target.Support(normal), normal) + target.Radius
        - (Vector2.Dot(moving.Support(-normal), normal) - moving.Radius);

    // Whether a one-way collider stops a mover meeting it with this normal. It blocks only on a surface
    // it keeps, and only a mover that started clear of it.
    private static bool OneWayBlocks(in Shape2D moving, in Shape2D target, Vector2 normal, bool solidSides) =>
        CollisionGrid2D.OneWayKeeps(normal, solidSides) && DepthPast(moving, target, normal) <= CollisionTolerance.LinearSlop;

    // A box and an axis-aligned segment take the closed form against a box mover. A slanted segment
    // has no box to stand for it.
    private static bool IsBoxLike(in Shape2D shape) =>
        shape.Kind == ShapeKind2D.Box
        || (shape.Kind == ShapeKind2D.Segment
            && (shape.Bounds.Min.X == shape.Bounds.Max.X || shape.Bounds.Min.Y == shape.Bounds.Max.Y));

    // How far apart two shapes are, negative when they overlap, with the surface point and the normal
    // on the second. A box and an axis-aligned edge take the closed form against a box mover.
    private static float Separation(in Shape2D shape, in Shape2D other, out Vector2 normal, out Vector2 point)
    {
        if (shape.Kind == ShapeKind2D.Box && IsBoxLike(other))
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

    // How far a pair overlaps, from its signed separation. A pair within the skin but apart overlaps by nothing.
    private static float DepthOf(float separation) => MathF.Max(0f, -separation);

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
        if (moving.Kind == ShapeKind2D.Box && IsBoxLike(target))
        {
            return SweepBoxes(moving.Bounds, translation, target.Bounds, out fraction, out normal, out point);
        }

        if (Gjk2D.ShapeCast(target, moving, translation, out fraction, out point, out normal))
        {
            normal = FaceNormal(moving, translation * fraction, target, normal);
            return true;
        }

        if (Separation(moving, target, out normal, out point) > CollisionTolerance.ContactSkin)
        {
            return false;
        }

        normal = FaceNormal(moving, Vector2.Zero, target, normal);
        if (Vector2.Dot(translation, normal) >= 0f)
        {
            return false;
        }

        fraction = 0f;
        point = Vector2.Clamp(moving.Bounds.Center, target.Bounds.Min, target.Bounds.Max);

        return true;
    }

    // The face normal nearest `normal` among the faces of two sharp hulls that still separate them,
    // with moving displaced by `offset` to where it touches. Two corners meeting leave GJK a direction
    // between them that neither surface has. Hulls that already overlap take the face they overlap
    // least along, where GJK's answer only approximates that face. A rounded shape has every such
    // direction on its surface, and a segment has its own rule in EdgeNormal.
    private static Vector2 FaceNormal(in Shape2D moving, Vector2 offset, in Shape2D target, Vector2 normal)
    {
        if (moving.Radius != 0f || target.Radius != 0f || moving.PointCount < 3 || target.PointCount < 3 || normal == Vector2.Zero)
        {
            return normal;
        }

        Vector2 best = normal;
        float nearest = float.NegativeInfinity;
        Vector2 shallowest = normal;
        float least = float.NegativeInfinity;
        Faces(target, moving, offset, target, 1f, normal, ref best, ref nearest, ref shallowest, ref least);
        Faces(moving, moving, offset, target, -1f, normal, ref best, ref nearest, ref shallowest, ref least);

        return nearest > float.NegativeInfinity ? best : shallowest;
    }

    // Keeps the outward normal of each face of `hull`, turned by `sign` to point from target to moving,
    // that lies nearest `normal` and along which the pair is at most a skin into each other. It also
    // keeps the face along which the pair reaches least far into each other.
    private static void Faces(
        in Shape2D hull,
        in Shape2D moving,
        Vector2 offset,
        in Shape2D target,
        float sign,
        Vector2 normal,
        ref Vector2 best,
        ref float nearest,
        ref Vector2 shallowest,
        ref float least)
    {
        for (int index = 0; index < hull.PointCount; index++)
        {
            Vector2 face = sign * hull.EdgeNormal(index);
            float alignment = Vector2.Dot(face, normal);
            float separation = Vector2.Dot(moving.Support(-face) + offset - target.Support(face), face);
            if (separation > least)
            {
                least = separation;
                shallowest = face;
            }

            if (alignment > nearest && separation >= -CollisionTolerance.ContactSkin)
            {
                nearest = alignment;
                best = face;
            }
        }
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
        CollisionGrid2D grid,
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
            && !IsIgnored(ignore, index, slot.Generation)
            && index != _passThrough;
    }

    // Walks the grids and then the tree. An empty span takes the nearest hit into the accumulator,
    // and any other span takes the nearest hits that fit.
    private RayAccumulator RayWalk(
        Vector2 origin,
        Vector2 unit,
        float distance,
        CollisionFilter filter,
        ColliderHandle ignore,
        Span<RayHit2D> hits,
        out int count)
    {
        RayAccumulator accumulator = new() { Distance = distance };
        count = 0;

        foreach (CollisionGrid2D grid in Grids)
        {
            if (grid.Handle == ignore || (filter & grid.Layers).IsEmpty)
            {
                continue;
            }

            if (!Rays2D.RayBoxRange(grid.Bounds, origin, unit, accumulator.Distance, out float enter, out float exit, out _))
            {
                continue;
            }

            WalkGrid(grid, origin, unit, enter, exit, filter, grid.AdmitsEveryLayer(filter), ref accumulator, hits, ref count);
        }

        RayVisitor visitor = new(this, origin, unit, filter, ignore, hits, count, accumulator);
        _tree.RayCast(origin, unit, accumulator.Distance, filter.Bits, ref visitor);
        count = visitor.Count;

        return visitor.Accumulator;
    }

    // Amanatides and Woo. A ray touches only the cells it crosses, in the order it crosses them.
    private void WalkGrid(
        CollisionGrid2D grid,
        Vector2 origin,
        Vector2 unit,
        float enter,
        float exit,
        CollisionFilter filter,
        bool admitsEvery,
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count)
    {
        bool all = !hits.IsEmpty;
        int size = grid.CellSize;
        Vector2 start = origin + (unit * enter);
        int x = Math.Clamp(CollisionGrid2D.FloorDiv(start.X, size), 0, grid.Width - 1);
        int y = Math.Clamp(CollisionGrid2D.FloorDiv(start.Y, size), 0, grid.Height - 1);

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
            if (TestCell(grid, x, y, origin, unit, filter, admitsEvery, ref accumulator, hits, ref count) && !all)
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
                && TestCell(grid, x, y + stepY, origin, unit, filter, admitsEvery, ref accumulator, hits, ref count)
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
        CollisionGrid2D grid,
        int x,
        int y,
        Vector2 origin,
        Vector2 unit,
        CollisionFilter filter,
        bool admitsEvery,
        ref RayAccumulator accumulator,
        Span<RayHit2D> hits,
        ref int count)
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
        else if (!FirstEdgeCrossed(grid, x, y, state, origin, unit, limit, filter, admitsEvery, out t, out normal))
        {
            return false;
        }

        RecordRay(
            ref accumulator,
            hits,
            ref count,
            t,
            CollisionTarget.ForGridCell(grid.Handle, x, y, layer),
            normal,
            origin,
            unit);

        return true;
    }

    // The first live edge of an edge cell the ray crosses inwards. A ray travelling along an edge or away
    // from it cannot cross it, which makes an edge one-sided. A ray starting inside a solid polygon hits
    // at 0 on its nearest edge, as a ray starting inside a box does.
    private static bool FirstEdgeCrossed(
        CollisionGrid2D grid,
        int x,
        int y,
        CellState2D state,
        Vector2 origin,
        Vector2 unit,
        float limit,
        CollisionFilter filter,
        bool admitsEvery,
        out float t,
        out Vector2 normal)
    {
        t = 0f;
        normal = Vector2.Zero;
        ReadOnlySpan<CellEdge2D> edges = grid.EdgesAt(x, y);
        Vector2 corner = grid.CellCorner(x, y);

        if ((state & CellState2D.OneWay) == 0 && Inside(edges, origin - corner, out normal))
        {
            return true;
        }

        float nearest = float.PositiveInfinity;
        for (int index = 0; index < edges.Length; index++)
        {
            CellEdge2D edge = edges[index];
            if (Vector2.Dot(unit, edge.Normal) >= 0f
                || !grid.EdgeLive(x, y, state, index, edge, filter, admitsEvery))
            {
                continue;
            }

            if (Rays2D.RaySegment(corner + edge.Start, corner + edge.End, origin, unit, limit, out float edgeT) && edgeT < nearest)
            {
                nearest = edgeT;
                normal = edge.Normal;
            }
        }

        if (float.IsPositiveInfinity(nearest))
        {
            return false;
        }

        t = nearest;

        return true;
    }

    // Whether a cell-space point lies strictly inside a solid polygon, and the normal of its nearest edge.
    private static bool Inside(ReadOnlySpan<CellEdge2D> edges, Vector2 point, out Vector2 normal)
    {
        normal = Vector2.Zero;
        float nearest = float.NegativeInfinity;

        foreach (CellEdge2D edge in edges)
        {
            float outside = Vector2.Dot(point - edge.Start, edge.Normal);
            if (outside >= 0f)
            {
                normal = Vector2.Zero;
                return false;
            }

            if (outside > nearest)
            {
                nearest = outside;
                normal = edge.Normal;
            }
        }

        return true;
    }

    private int FindContacts(
        in Shape2D world,
        CollisionFilter filter,
        float tolerance,
        ColliderHandle ignore,
        Span<Contact2D> contacts)
    {
        int written = FindGridContacts(world, filter, tolerance, ignore, contacts, out int found);
        TouchVisitor visitor = new(this, world, filter, tolerance, ignore, contacts, written, found);
        _tree.Query(world.Bounds.Expanded(tolerance), filter.Bits, ref visitor);

        return visitor.Found;
    }

    // Writes the grid cells within tolerance of a shape, and returns how many were written. found is
    // how many there were.
    private int FindGridContacts(
        in Shape2D world,
        CollisionFilter filter,
        float tolerance,
        ColliderHandle ignore,
        Span<Contact2D> contacts,
        out int found)
    {
        found = 0;
        int written = 0;
        Aabb2D probe = world.Bounds.Expanded(tolerance);

        foreach (CollisionGrid2D grid in Grids)
        {
            if (grid.Handle == ignore || (filter & grid.Layers).IsEmpty || !grid.Bounds.Overlaps(probe))
            {
                continue;
            }

            bool admitsEvery = grid.AdmitsEveryLayer(filter);
            int size = grid.CellSize;
            int minX = Math.Max(0, CollisionGrid2D.FloorDiv(probe.Min.X, size));
            int maxX = Math.Min(grid.Width - 1, CollisionGrid2D.FloorDiv(probe.Max.X, size));
            int minY = Math.Max(0, CollisionGrid2D.FloorDiv(probe.Min.Y, size));
            int maxY = Math.Min(grid.Height - 1, CollisionGrid2D.FloorDiv(probe.Max.Y, size));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!TryCell(grid, x, y, filter, out CellState2D state, out CollisionLayer layer)
                        || !CellContact(grid, x, y, state, world, tolerance, filter, admitsEvery, out Vector2 normal, out Vector2 point, out float separation))
                    {
                        continue;
                    }

                    found++;
                    if (written < contacts.Length)
                    {
                        contacts[written++] = new Contact2D(
                            CollisionTarget.ForGridCell(grid.Handle, x, y, layer),
                            point,
                            normal,
                            DepthOf(separation));
                    }
                }
            }
        }

        return written;
    }

    // Whether a shape is within tolerance of a cell, and where. A cell reports one contact, the nearest,
    // however many edges it carries. A solid cell is measured as its whole shape, and a one-way cell
    // edge by edge.
    private static bool CellContact(
        CollisionGrid2D grid,
        int x,
        int y,
        CellState2D state,
        in Shape2D world,
        float tolerance,
        CollisionFilter filter,
        bool admitsEvery,
        out Vector2 normal,
        out Vector2 point,
        out float nearest)
    {
        if ((state & CellState2D.Solid) != 0)
        {
            nearest = Separation(world, grid.CellBox(x, y), out normal, out point);

            return nearest <= tolerance;
        }

        return EdgeContact(grid, x, y, state, world, tolerance, filter, admitsEvery, out normal, out point, out nearest);
    }

    // The same for a polygon or one-way cell, kept apart from CellContact, whose box path is the hot one.
    private static bool EdgeContact(
        CollisionGrid2D grid,
        int x,
        int y,
        CellState2D state,
        in Shape2D world,
        float tolerance,
        CollisionFilter filter,
        bool admitsEvery,
        out Vector2 normal,
        out Vector2 point,
        out float nearest)
    {
        Vector2 corner = grid.CellCorner(x, y);
        if ((state & CellState2D.OneWay) == 0)
        {
            nearest = Separation(world, grid.PolygonAt(x, y).Translated(corner), out normal, out point);

            return nearest <= tolerance;
        }

        normal = Vector2.Zero;
        point = Vector2.Zero;
        nearest = float.PositiveInfinity;

        ReadOnlySpan<CellEdge2D> edges = grid.EdgesAt(x, y);
        for (int index = 0; index < edges.Length; index++)
        {
            CellEdge2D edge = edges[index];
            if (!grid.EdgeLive(x, y, state, index, edge, filter, admitsEvery))
            {
                continue;
            }

            Vector2 start = corner + edge.Start;
            float separation = Separation(world, Shape2D.Segment(start, corner + edge.End), out _, out Vector2 edgePoint);

            // An edge is a surface only to a shape on its outward side. The authored line decides that
            // side, not the narrowphase, whose least-penetration axis resolves a tie towards -X and -Y.
            // The test is inclusive, so a centre on the line counts as outward.
            if (separation <= tolerance
                && separation < nearest
                && Vector2.Dot(world.Bounds.Center - start, edge.Normal) >= 0f)
            {
                nearest = separation;
                normal = edge.Normal;
                point = edgePoint;
            }
        }

        return !float.IsPositiveInfinity(nearest);
    }

    // A band holding more hits than the world's scratch has room for walks the sweep twice. The first
    // walk finds the primary, and the second writes every hit within the band of it. Neither walk
    // allocates, however many surfaces one sweep meets at once.
    private void Cast(
        in Shape2D moving,
        Vector2 translation,
        CollisionFilter filter,
        ColliderHandle ignore,
        Span<Contact2D> contacts,
        bool throughOneWay,
        ref CastAccumulator accumulator)
    {
        accumulator.Length = translation.Length();
        accumulator.Lean = -Along * accumulator.Length;
        _bandCount = 0;
        Gather(moving, translation, filter, ignore, contacts, throughOneWay, ref accumulator);

        if (!accumulator.Overflowed)
        {
            WriteBand(ref accumulator, contacts);
            return;
        }

        accumulator.Anchored = true;
        Gather(moving, translation, filter, ignore, contacts, throughOneWay, ref accumulator);
    }

    // Walks the grids and then the tree for everything the sweep meets.
    private void Gather(
        in Shape2D moving,
        Vector2 translation,
        CollisionFilter filter,
        ColliderHandle ignore,
        Span<Contact2D> contacts,
        bool throughOneWay,
        ref CastAccumulator accumulator)
    {
        Aabb2D start = moving.Bounds.Expanded(CollisionTolerance.LinearSlop);
        Aabb2D swept = start.Swept(translation);

        foreach (CollisionGrid2D grid in Grids)
        {
            if (grid.Handle == ignore || (filter & grid.Layers).IsEmpty || !grid.Bounds.Overlaps(swept))
            {
                continue;
            }

            bool admitsEvery = grid.AdmitsEveryLayer(filter);
            int size = grid.CellSize;
            int width = grid.Width;
            int minX = Math.Max(0, CollisionGrid2D.FloorDiv(swept.Min.X, size));
            int maxX = Math.Min(width - 1, CollisionGrid2D.FloorDiv(swept.Max.X, size));
            ReadOnlySpan<CellState2D> states = grid.States;

            // Column by column, and within each only the rows the sweep passes through. That band is
            // narrower than the bounding rectangle of a long diagonal. ColumnRows keeps the band on the
            // grid, and an empty cell is passed over without a call.
            for (int x = minX; x <= maxX; x++)
            {
                if (!ColumnRows(start, translation, x, size, grid.Height, out int minY, out int maxY))
                {
                    continue;
                }

                GridCellsTested += maxY - minY + 1;
                for (int y = minY, cell = (minY * width) + x; y <= maxY; y++, cell += width)
                {
                    CellState2D state = states[cell];
                    if (state != CellState2D.None)
                    {
                        CastCell(grid, x, y, state, moving, translation, filter, admitsEvery, throughOneWay, contacts, ref accumulator);
                    }
                }
            }
        }

        CastVisitor visitor = new(this, moving, translation, filter, ignore, throughOneWay, contacts, accumulator);
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

        minY = Math.Max(0, CollisionGrid2D.FloorDiv(top, size));
        maxY = Math.Min(height - 1, CollisionGrid2D.FloorDiv(bottom, size));

        return minY <= maxY;
    }

    // Casts against one colliding cell of the grid, which the caller has already counted as tested.
    private void CastCell(
        CollisionGrid2D grid,
        int x,
        int y,
        CellState2D state,
        in Shape2D moving,
        Vector2 translation,
        CollisionFilter filter,
        bool admitsEvery,
        bool throughOneWay,
        Span<Contact2D> contacts,
        ref CastAccumulator accumulator)
    {
        CollisionLayer layer = grid.LayerOf(x, y);
        if (!filter.Admits(layer))
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

            RecordCast(ref accumulator, contacts, translation, fraction, target, normal, point);

            return;
        }

        // A drop passes the top of a one-way cell. The walls of a solid-sided one still stand.
        bool dropping = throughOneWay && (state & CellState2D.OneWay) != 0;
        if (!dropping || (state & CellState2D.SolidSides) != 0)
        {
            CastEdges(grid, x, y, state, target, moving, translation, filter, admitsEvery, dropping, contacts, ref accumulator);
        }
    }

    // The edges of a polygon or one-way cell, each cast as a segment, less the up-facing ones while
    // dropping. Kept apart from CastCell, whose box path is the hot one.
    private void CastEdges(
        CollisionGrid2D grid,
        int x,
        int y,
        CellState2D state,
        in CollisionTarget target,
        in Shape2D moving,
        Vector2 translation,
        CollisionFilter filter,
        bool admitsEvery,
        bool dropping,
        Span<Contact2D> contacts,
        ref CastAccumulator accumulator)
    {
        ReadOnlySpan<CellEdge2D> edges = grid.EdgesAt(x, y);
        Vector2 corner = grid.CellCorner(x, y);

        // A one-way cell without solid sides is a surface only to a mover wholly above its line. The
        // ends of its edges stop nothing that arrives from below or beside them.
        bool topOnly = (state & (CellState2D.OneWay | CellState2D.SolidSides)) == CellState2D.OneWay;
        for (int index = 0; index < edges.Length; index++)
        {
            CellEdge2D edge = edges[index];
            Vector2 outward = edge.Normal;
            Vector2 start = corner + edge.Start;

            // An edge stops only a sweep crossing it inwards that began on its outward side. A sweep
            // running along it never tests it, so a slide carries over the join of two slopes.
            Vector2 end = corner + edge.End;
            if (Vector2.Dot(translation, outward) >= accumulator.Lean
                || (dropping && outward.Y < -CollisionGrid2D.UpFacing)
                || !grid.EdgeLive(x, y, state, index, edge, filter, admitsEvery)
                || (topOnly
                    ? DepthPast(moving, start, outward) > CollisionTolerance.LinearSlop
                    : StartedPast(moving, start, end, outward)))
            {
                continue;
            }

            // An axis-aligned edge is a zero-thickness box, which a box mover sweeps in closed form.
            bool swept = start.X == end.X || start.Y == end.Y
                ? Sweep(moving, translation, new Aabb2D(Vector2.Min(start, end), Vector2.Max(start, end)), out float fraction, out Vector2 normal, out Vector2 point)
                : Sweep(moving, translation, Shape2D.Segment(start, end), out fraction, out normal, out point);

            if (!swept || Vector2.Dot(normal, outward) <= 0f)
            {
                continue;
            }

            // Report the edge's own normal, unless a box face met its end. A rounded shape meeting the
            // end of an edge is nearest its endpoint, where GJK answers with a diagonal the authored line
            // does not have.
            RecordCast(ref accumulator, contacts, translation, fraction, target, topOnly ? outward : EdgeNormal(moving, normal, outward), point);
        }
    }

    // Moves a shape as far along a translation as it can go and slides the rest along what stopped it.
    // Contacts are written pass by pass from the start of the span.
    private MoveResult2D Slide(
        in Shape2D shape,
        Vector2 origin,
        Vector2 translation,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore)
    {
        MoveSweep sweep = new(this, shape, origin, filter, ignore, false, contacts);
        sweep.Slide(translation);

        return sweep.Result;
    }

    // One sweep of a move. The shape runs from `at` along the translation to the first band of surfaces
    // and stops a slop short of them along their normal. A surface reached exactly at the end of the
    // translation stopped nothing. Contacts are written from the start of the span.
    internal MovePass Pass(
        in Shape2D shape,
        Vector2 at,
        Vector2 translation,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore,
        bool throughOneWay)
    {
        if (translation == Vector2.Zero)
        {
            return default;
        }

        Shape2D moving = shape.Translated(at);

        if (moving.Kind == ShapeKind2D.Box && (translation.X == 0f || translation.Y == 0f))
        {
            // Shrunk on the axis it is not moving along. A face flush with its side then does not read
            // as an obstacle, and a slide along a flat run cannot catch on a seam. A rounded shape needs
            // no inset, since its advance already stops short of a tangent surface.
            bool horizontal = translation.Y == 0f;
            Aabb2D bounds = moving.Bounds;
            Vector2 size = bounds.Size;
            float across = horizontal ? size.Y : size.X;
            float inset = MathF.Max(0f, MathF.Min(CollisionTolerance.LinearSlop, (across - (2f * Shape2D.PointTolerance)) * 0.5f));
            Vector2 shrink = horizontal ? new Vector2(0f, inset) : new Vector2(inset, 0f);
            moving = Shape2D.Box(new Aabb2D(bounds.Min + shrink, bounds.Max - shrink));
        }

        CastAccumulator accumulator = default;
        Cast(moving, translation, filter, ignore, contacts, throughOneWay, ref accumulator);

        if (!accumulator.Hit || accumulator.Fraction >= 1f)
        {
            return new MovePass(translation, false, Vector2.Zero, accumulator.Written, accumulator.Found);
        }

        // Stop a slop short of the surface along its normal. Rounding error then cannot leave the mover
        // inside it, and the gap is within the contact skin, so the surface still reports as touched.
        // The mover never backs up past where it started.
        float length = accumulator.Length;
        float approach = -Vector2.Dot(translation, accumulator.Normal) / length;
        float travel = (length * accumulator.Fraction) - (CollisionTolerance.LinearSlop / approach);
        Vector2 moved = travel > 0f ? translation * (travel / length) : Vector2.Zero;

        return new MovePass(moved, true, accumulator.Normal, accumulator.Written, accumulator.Found);
    }

    private ref struct RayVisitor(
        CollisionWorld2D world,
        Vector2 origin,
        Vector2 unit,
        CollisionFilter filter,
        ColliderHandle ignore,
        Span<RayHit2D> hits,
        int count,
        RayAccumulator accumulator) : IRayVisitor2D
    {
        private readonly Span<RayHit2D> _hits = hits;

        internal int Count = count;
        internal RayAccumulator Accumulator = accumulator;

        public float Visit(int proxyId, float maxFraction)
        {
            if (!world.TryProxy(proxyId, filter, ignore, out int index))
            {
                return maxFraction;
            }

            ref ColliderSlot slot = ref world._slots[index];

            // A one-way collider meets only a ray arriving from outside on a surface it keeps.
            if (!Rays2D.RayShape(slot.World, origin, unit, Accumulator.Distance, out float t, out Vector2 normal)
                || (slot.OneWay && !(t > 0f && CollisionGrid2D.OneWayKeeps(normal, slot.SolidSides))))
            {
                return maxFraction;
            }

            RecordRay(
                ref Accumulator,
                _hits,
                ref Count,
                t,
                CollisionTarget.ForCollider(world.HandleAt(index), slot.Layer),
                normal,
                origin,
                unit);

            return Accumulator.Distance;
        }
    }

    // Writes the handles of every shape collider whose broadphase box meets `box` in handle order,
    // skipping grids and `ignore`, and returns how many there were. A collider's shove gathers its
    // candidates here.
    internal int CollidersNear(in Aabb2D box, ColliderHandle ignore, Span<ColliderHandle> found)
    {
        NearVisitor visitor = new(this, ignore, found);
        _tree.Query(box, ulong.MaxValue, ref visitor);

        return visitor.Found;
    }

    // Sweeps a collider's shape from `from` along `translation` against one other collider standing
    // `offset` from where it is registered, and reports the fraction at which it drives into it, with
    // the point and the target's normal there. A pair the sweep runs along or away from is no hit.
    internal bool SweepPair(
        ColliderHandle mover,
        Vector2 from,
        Vector2 translation,
        ColliderHandle target,
        Vector2 offset,
        out float fraction,
        out Vector2 normal,
        out Vector2 point)
    {
        ref ColliderSlot pusher = ref _slots[RequireShapeSlot(mover)];
        Shape2D moving = pusher.Local.Translated(from);
        ref ColliderSlot other = ref _slots[RequireShapeSlot(target)];

        return offset == Vector2.Zero
            ? SweepPair(pusher, moving, translation, other.World, out fraction, out normal, out point)
            : SweepPair(pusher, moving, translation, other.World.Translated(offset), out fraction, out normal, out point);
    }

    // A one-way pusher meets a body as the body would meet it, so the body must start clear of the
    // pusher's surface. The sweep reports the body's normal, and the pusher's surface faces the other way.
    private static bool SweepPair(
        in ColliderSlot pusher,
        in Shape2D moving,
        Vector2 translation,
        in Shape2D target,
        out float fraction,
        out Vector2 normal,
        out Vector2 point) =>
        Sweep(moving, translation, target, out fraction, out normal, out point)
            && Vector2.Dot(translation, normal) < -Along * translation.Length()
            && (!pusher.OneWay || OneWayBlocks(target, moving, -normal, pusher.SolidSides));

    // Moves a shape as Move does, also passing through `pusher`. A shove sweeps the body it moves this
    // way, because a pusher that outran the body already lies across the body's path.
    internal MoveResult2D MovePast(
        in Shape2D shape,
        Vector2 origin,
        Vector2 translation,
        CollisionFilter filter,
        ColliderHandle ignore,
        ColliderHandle pusher)
    {
        _passThrough = RequireShapeSlot(pusher);
        try
        {
            return Slide(shape, origin, translation, filter, default, ignore);
        }
        finally
        {
            _passThrough = -1;
        }
    }

    private ref struct NearVisitor(CollisionWorld2D world, ColliderHandle ignore, Span<ColliderHandle> found) : ITreeVisitor2D
    {
        private readonly Span<ColliderHandle> _found = found;

        // How many colliders the box met, span or no span.
        internal int Found;

        public bool Visit(int proxyId)
        {
            if (!world.TryProxy(proxyId, CollisionFilter.Everything, ignore, out int index))
            {
                return true;
            }

            // Kept in handle order, and a short span keeps the lowest handles. The order a shove
            // pushes in then does not depend on the tree's arrangement.
            int written = Math.Min(Found, _found.Length);
            Found++;
            if (written == _found.Length)
            {
                if (written == 0 || _found[written - 1].Index <= index)
                {
                    return true;
                }

                written--;
            }

            int position = written;
            while (position > 0 && _found[position - 1].Index > index)
            {
                _found[position] = _found[position - 1];
                position--;
            }

            _found[position] = world.HandleAt(index);

            return true;
        }
    }

    private ref struct TouchVisitor(
        CollisionWorld2D world,
        Shape2D shape,
        CollisionFilter filter,
        float tolerance,
        ColliderHandle ignore,
        Span<Contact2D> contacts,
        int written,
        int found) : ITreeVisitor2D
    {
        private readonly Span<Contact2D> _contacts = contacts;

        private readonly int _first = written;
        private int _written = written;

        // How many overlaps there are, span or no span.
        internal int Found = found;

        public bool Visit(int proxyId)
        {
            if (!world.TryProxy(proxyId, filter, ignore, out int index))
            {
                return true;
            }

            ref ColliderSlot slot = ref world._slots[index];

            float separation = Separation(shape, slot.World, out Vector2 normal, out Vector2 point);
            if (separation > tolerance)
            {
                return true;
            }

            Found++;
            InsertByHandle(
                _contacts,
                _first,
                ref _written,
                new Contact2D(CollisionTarget.ForCollider(world.HandleAt(index), slot.Layer), point, normal, DepthOf(separation)));

            return true;
        }
    }

    private ref struct CastVisitor(
        CollisionWorld2D world,
        Shape2D moving,
        Vector2 translation,
        CollisionFilter filter,
        ColliderHandle ignore,
        bool throughOneWay,
        Span<Contact2D> contacts,
        CastAccumulator accumulator) : ITreeVisitor2D
    {
        private readonly Span<Contact2D> _contacts = contacts;

        internal CastAccumulator Accumulator = accumulator;

        public bool Visit(int proxyId)
        {
            if (!world.TryProxy(proxyId, filter, ignore, out int index))
            {
                return true;
            }

            ref ColliderSlot slot = ref world._slots[index];

            if (!Sweep(moving, translation, slot.World, out float fraction, out Vector2 normal, out Vector2 point)
                || (slot.OneWay && !OneWayBlocks(moving, slot.World, normal, slot.SolidSides))
                || (slot.OneWay && throughOneWay && normal.Y < -CollisionGrid2D.UpFacing))
            {
                return true;
            }

            world.RecordCast(
                ref Accumulator,
                _contacts,
                translation,
                fraction,
                CollisionTarget.ForCollider(world.HandleAt(index), slot.Layer),
                normal,
                point);

            return true;
        }
    }
}
