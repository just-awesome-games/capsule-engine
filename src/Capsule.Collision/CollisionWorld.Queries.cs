using System.Numerics;
using Capsule.Collision.Internal;

namespace Capsule.Collision;

/// <summary>The traversal behind the query seam: the grid walk, the tree walk, and the mover.</summary>
public sealed partial class CollisionWorld
{
    // Sweep fractions this close together are one moment: a box landing squarely on a run of tiles
    // meets several faces at once, and every one of them is what stopped it.
    private const float FractionBand = 1e-4f;

    private static void RecordRay(
        ref RayAccumulator accumulator,
        Span<RayHit> hits,
        ref int count,
        bool all,
        float distance,
        in CollisionTarget target,
        Vector2 normal,
        Vector2 origin,
        Vector2 unit)
    {
        if (all)
        {
            Insert(hits, ref count, new RayHit(target, origin + (unit * distance), normal, distance));

            // A full span is also the limit: nothing past its farthest entry can join the result,
            // so the grid walk and the tree walk both stop looking beyond it from here on.
            if (count == hits.Length)
            {
                accumulator.Distance = hits[count - 1].Distance;
            }

            return;
        }

        if (accumulator.Hit && distance >= accumulator.Distance)
        {
            return;
        }

        accumulator.Hit = true;
        accumulator.Distance = distance;
        accumulator.Normal = normal;
        accumulator.Target = target;
    }

    // The span holds the nearest hits found so far, in order. A full span gives up its farthest
    // entry to a nearer one rather than dropping the newcomer, so which hits survive depends on
    // where they are and never on the order the traversal happened to meet them.
    private static void Insert(Span<RayHit> hits, ref int count, in RayHit hit)
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

    // Nearest first, and then a tie-break that reads nothing from the tree's current arrangement:
    // tiles before colliders, then by slot, then by cell. Two runs over the same world must fill
    // the same span with the same hits in the same order.
    private static bool Precedes(in RayHit left, in RayHit right)
    {
        if (left.Distance != right.Distance)
        {
            return left.Distance < right.Distance;
        }

        if (left.Target.IsGridCell != right.Target.IsGridCell)
        {
            return left.Target.IsGridCell;
        }

        if (left.Target.Collider.Index != right.Target.Collider.Index)
        {
            return left.Target.Collider.Index < right.Target.Collider.Index;
        }

        return left.Target.CellY != right.Target.CellY
            ? left.Target.CellY < right.Target.CellY
            : left.Target.CellX < right.Target.CellX;
    }

    private static void Consider(
        ref CastAccumulator accumulator,
        Span<Contact> contacts,
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
            return;
        }

        if (!accumulator.Hit || fraction < accumulator.Fraction - FractionBand)
        {
            accumulator.Hit = true;
            accumulator.Fraction = fraction;
            accumulator.Normal = normal;
            accumulator.Point = point;
            accumulator.Target = target;
            accumulator.Count = 0;
        }
        else if (fraction > accumulator.Fraction + FractionBand)
        {
            return;
        }

        if (accumulator.Count < contacts.Length)
        {
            contacts[accumulator.Count++] = new Contact(target, point, normal);
        }
    }

    // Whether a slot is the one the query was told to pass through. The generation is half the
    // answer: an index alone still names the slot after its collider is removed, and the next
    // collider to take that slot would be suppressed in its place. The seams reject a stale ignore
    // outright, so this is the invariant holding structurally rather than a reachable case.
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

    // Whether a solid cell's face is a surface this query can meet. The grid's own face culling is
    // geometric — a face shared with a solid neighbour is interior — and that is exactly right only
    // while the query admits every tag the grid uses. A filter that excludes a tag turns those
    // cells into empty space, faces included, so an interior face bordering one becomes a real
    // surface. The tag test costs nothing in the ordinary case: it is reached only for a face the
    // grid already culled, on a grid the filter does not wholly admit.
    private static bool IsActiveFace(
        GridCollider map,
        int x,
        int y,
        CellState state,
        Vector2 normal,
        CollisionFilter filter) =>
        IsBoundaryFace(state, normal)
        || (!map.AdmitsEveryTag(filter) && !map.NeighbourAdmits(x, y, normal, filter));

    // A one-way tile's edge and a solid tile's body are both handed over as boxes; a zero-height
    // one is the segment it looks like, which the hull routines read directly.
    private static Shape AsShape(in Aabb box) =>
        box.Min.Y == box.Max.Y ? Shape.Segment(box.Min, box.Max) : Shape.Box(box);

    private static float SeparationOf(in Shape shape, in Shape other, out Vector2 normal, out Vector2 point)
    {
        if (shape.Kind == ShapeKind.Box && other.Kind == ShapeKind.Box)
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

    // A tile is an axis-aligned box even when it is the zero-height one a one-way edge is, so a box
    // mover meets terrain through the closed-form sweep and never through the iterated one.
    private static bool SweepAgainstCell(
        in Shape moving,
        Vector2 translation,
        in Aabb target,
        out float fraction,
        out Vector2 normal,
        out Vector2 point)
    {
        if (moving.Kind != ShapeKind.Box)
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

    private static float SeparationOfCell(in Shape shape, in Aabb cell, out Vector2 normal, out Vector2 point) =>
        shape.Kind == ShapeKind.Box
            ? Boxes.Separation(shape.Bounds, cell, out normal, out point)
            : SeparationOf(shape, AsShape(cell), out normal, out point);

    private static bool SweepAgainst(
        in Shape moving,
        Vector2 translation,
        in Shape target,
        out float fraction,
        out Vector2 normal,
        out Vector2 point)
    {
        if (moving.Kind == ShapeKind.Box && target.Kind == ShapeKind.Box)
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

    // The tree hands proxies back in whatever shape it currently has, so the collider half of a
    // result set is ordered by handle here and the caller never sees the tree's own arrangement.
    private static void SortByHandle(Span<Contact> contacts)
    {
        for (int index = 1; index < contacts.Length; index++)
        {
            Contact candidate = contacts[index];
            int position = index - 1;
            while (position >= 0 && contacts[position].Target.Collider.Index > candidate.Target.Collider.Index)
            {
                contacts[position + 1] = contacts[position];
                position--;
            }

            contacts[position + 1] = candidate;
        }
    }

    private void RaycastGrids(
        Vector2 origin,
        Vector2 unit,
        CollisionFilter filter,
        ColliderHandle ignore,
        ref RayAccumulator accumulator,
        Span<RayHit> hits,
        ref int count)
    {
        bool all = !hits.IsEmpty;

        foreach (GridCollider map in _grids)
        {
            if (map.Handle == ignore || (filter & map.Tags).IsEmpty)
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

    // Amanatides and Woo: the grid is the broadphase, so a ray touches only the cells it crosses,
    // in the order it crosses them.
    private void WalkGrid(
        GridCollider map,
        Vector2 origin,
        Vector2 unit,
        float enter,
        float exit,
        CollisionFilter filter,
        ref RayAccumulator accumulator,
        Span<RayHit> hits,
        ref int count,
        bool all)
    {
        int size = map.CellSize;
        Vector2 start = origin + (unit * enter);
        int x = Math.Clamp(GridCollider.FloorDiv(start.X, size), 0, map.Width - 1);
        int y = Math.Clamp(GridCollider.FloorDiv(start.Y, size), 0, map.Height - 1);

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

            // Re-read every step, because it tightens as hits are taken: once a bounded RaycastAll
            // has filled its span, nothing past its farthest entry can join the result, and a walk
            // that kept going to the grid's far edge would cell-test the rest of the map for
            // nothing. Strictly greater, so a cell entered exactly at the limit is still tested and
            // the total order decides the tie rather than the traversal.
            if (MathF.Min(boundaryX, boundaryY) > MathF.Min(exit, accumulator.Distance))
            {
                return;
            }

            // An exact tie is the ray passing through a cell corner rather than across an edge, and
            // both cells that corner separates are entered at that one moment. Stepping X and
            // leaving it there would commit the walk to whichever face the box test picks — and at
            // a corner that can be the seam the two cells share, hiding the exposed face of the one
            // never visited. The other cell is tested here, before the walk goes on through it.
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
        GridCollider map,
        int x,
        int y,
        Vector2 origin,
        Vector2 unit,
        CollisionFilter filter,
        ref RayAccumulator accumulator,
        Span<RayHit> hits,
        ref int count,
        bool all)
    {
        CellState state = map.StateAt(x, y);
        if (state == CellState.None)
        {
            return false;
        }

        CollisionTag tag = map.TagOf(x, y);
        if (!filter.Matches(tag))
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
        else
        {
            if (unit.Y <= 0f)
            {
                return false;
            }

            Aabb edge = map.OneWayEdge(x, y);
            if (!Segments.RaySegment(edge.Min, edge.Max, origin, unit, limit, out t))
            {
                return false;
            }

            normal = new Vector2(0f, -1f);
        }

        RecordRay(
            ref accumulator,
            hits,
            ref count,
            all,
            t,
            CollisionTarget.ForGridCell(map.Handle, x, y, tag),
            normal,
            origin,
            unit);

        return true;
    }

    private void RaycastColliders(
        Vector2 origin,
        Vector2 unit,
        CollisionFilter filter,
        ColliderHandle ignore,
        ref RayAccumulator accumulator,
        Span<RayHit> hits,
        ref int count)
    {
        RayVisitor visitor = new(this, origin, unit, filter, ignore, hits, count, accumulator);
        _tree.RayCast(origin, unit, accumulator.Distance, ref visitor);

        count = visitor.Count;
        accumulator = visitor.Accumulator;
    }

    private int Touching(
        in Shape world,
        CollisionFilter filter,
        float tolerance,
        ColliderHandle ignore,
        Span<Contact> contacts)
    {
        int count = 0;
        Aabb probe = world.Bounds.Expanded(tolerance);

        foreach (GridCollider map in _grids)
        {
            if (map.Handle == ignore || (filter & map.Tags).IsEmpty || !map.Bounds.Overlaps(probe))
            {
                continue;
            }

            int size = map.CellSize;
            int minX = Math.Max(0, GridCollider.FloorDiv(probe.Min.X, size));
            int maxX = Math.Min(map.Width - 1, GridCollider.FloorDiv(probe.Max.X, size));
            int minY = Math.Max(0, GridCollider.FloorDiv(probe.Min.Y, size));
            int maxY = Math.Min(map.Height - 1, GridCollider.FloorDiv(probe.Max.Y, size));

            for (int y = minY; y <= maxY && count < contacts.Length; y++)
            {
                for (int x = minX; x <= maxX && count < contacts.Length; x++)
                {
                    CellState state = map.StateAt(x, y);
                    if (state == CellState.None)
                    {
                        continue;
                    }

                    CollisionTag tag = map.TagOf(x, y);
                    if (!filter.Matches(tag))
                    {
                        continue;
                    }

                    Aabb cell = (state & CellState.Solid) != 0 ? map.CellBox(x, y) : map.OneWayEdge(x, y);
                    if (SeparationOfCell(world, cell, out Vector2 normal, out Vector2 point) > tolerance)
                    {
                        continue;
                    }

                    contacts[count++] = new Contact(CollisionTarget.ForGridCell(map.Handle, x, y, tag), point, normal);
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

    private void Cast(
        in Shape moving,
        Vector2 translation,
        CollisionFilter filter,
        ColliderHandle ignore,
        Span<Contact> contacts,
        ref CastAccumulator accumulator)
    {
        Aabb start = moving.Bounds.Expanded(LinearSlop);
        Aabb swept = start.Swept(translation);

        // The one derived box both broadphases read: the grid floors it into cell coordinates and
        // the tree queries it directly. Checked here rather than at each seam because ShapeCast and
        // every axis of a move arrive through this one point.
        RequireFinite(swept, nameof(translation));

        foreach (GridCollider map in _grids)
        {
            if (map.Handle == ignore || (filter & map.Tags).IsEmpty || !map.Bounds.Overlaps(swept))
            {
                continue;
            }

            int size = map.CellSize;
            int minX = Math.Max(0, GridCollider.FloorDiv(swept.Min.X, size));
            int maxX = Math.Min(map.Width - 1, GridCollider.FloorDiv(swept.Max.X, size));

            // Column by column, and within each only the rows the sweep actually passes through.
            // The swept bounds of a long diagonal describe a rectangle the shape never enters most
            // of; walking the band keeps the cost proportional to the distance travelled.
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
    }

    // The rows one column of the grid shares with the swept shape. The sweep is inside the column's
    // slab over a single interval of the translation — the box's X range crosses each slab edge
    // once — and over that interval the shape's Y range is bounded by its position at the two ends,
    // so the band is exact rather than the bounding rectangle's full height.
    private static bool ColumnRows(
        in Aabb start,
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

        minY = Math.Max(0, GridCollider.FloorDiv(top, size));
        maxY = Math.Min(height - 1, GridCollider.FloorDiv(bottom, size));

        return minY <= maxY;
    }

    private void CastCell(
        GridCollider map,
        int x,
        int y,
        in Shape moving,
        Vector2 translation,
        CollisionFilter filter,
        Span<Contact> contacts,
        ref CastAccumulator accumulator)
    {
        CellState state = map.StateAt(x, y);
        if (state == CellState.None)
        {
            return;
        }

        CollisionTag tag = map.TagOf(x, y);
        if (!filter.Matches(tag))
        {
            return;
        }

        float fraction;
        Vector2 normal;
        Vector2 point;

        if ((state & CellState.Solid) != 0)
        {
            if (!SweepAgainstCell(moving, translation, map.CellBox(x, y), out fraction, out normal, out point))
            {
                return;
            }

            if (normal != Vector2.Zero && !IsActiveFace(map, x, y, state, normal, filter))
            {
                return;
            }
        }
        else
        {
            Aabb edge = map.OneWayEdge(x, y);

            // A one-way edge stops only a sweep crossing it towards +Y that began on the other
            // side of it; nothing else it could meet is a landing.
            if (translation.Y <= 0f || moving.Bounds.Max.Y > edge.Min.Y + LinearSlop)
            {
                return;
            }

            if (!SweepAgainstCell(moving, translation, edge, out fraction, out normal, out point)
                || normal.Y >= 0f)
            {
                return;
            }
        }

        Consider(
            ref accumulator,
            contacts,
            translation,
            fraction,
            CollisionTarget.ForGridCell(map.Handle, x, y, tag),
            normal,
            point);
    }

    private bool SweepAxis(
        in Shape shape,
        ref Vector2 at,
        float delta,
        bool horizontal,
        CollisionFilter filter,
        ColliderHandle ignore,
        Span<Contact> contacts,
        ref int written,
        out float moved)
    {
        moved = 0f;
        if (delta == 0f)
        {
            return false;
        }

        Vector2 translation = horizontal ? new Vector2(delta, 0f) : new Vector2(0f, delta);
        Shape moving = shape.Translated(at);

        if (moving.Kind == ShapeKind.Box)
        {
            // A box is shrunk on the axis it is not moving along, so a face exactly flush with its
            // side never reads as something in its way: this is what keeps a slide along a flat run
            // of tiles from catching on the seam between two of them. A rounded shape needs no
            // inset — the narrowphase's own advance already stops short of a tangent surface.
            Aabb bounds = moving.Bounds;
            Vector2 size = bounds.Size;
            float inset = horizontal
                ? MathF.Min(LinearSlop, size.Y * 0.25f)
                : MathF.Min(LinearSlop, size.X * 0.25f);
            Vector2 shrink = horizontal ? new Vector2(0f, inset) : new Vector2(inset, 0f);
            moving = Shape.Box(new Aabb(bounds.Min + shrink, bounds.Max - shrink));
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

        // Stopped a slop short of the surface rather than flush against it, so a rounding error at
        // the contact cannot leave the mover a hair inside; the gap is well within the contact
        // skin, so the surface still reports as touched on the next step.
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
        private readonly CollisionWorld _world;
        private readonly Vector2 _origin;
        private readonly Vector2 _unit;
        private readonly CollisionFilter _filter;
        private readonly ColliderHandle _ignore;
        private readonly Span<RayHit> _hits;

        internal RayVisitor(
            CollisionWorld world,
            Vector2 origin,
            Vector2 unit,
            CollisionFilter filter,
            ColliderHandle ignore,
            Span<RayHit> hits,
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

            if (!slot.InUse || slot.Grid is not null || !_filter.Matches(slot.Tag)
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
                CollisionTarget.ForCollider(_world.HandleAt(index), slot.Tag),
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
        private readonly CollisionWorld _world;
        private readonly Shape _shape;
        private readonly CollisionFilter _filter;
        private readonly float _tolerance;
        private readonly ColliderHandle _ignore;
        private readonly Span<Contact> _contacts;

        internal TouchVisitor(
            CollisionWorld world,
            in Shape shape,
            CollisionFilter filter,
            float tolerance,
            ColliderHandle ignore,
            Span<Contact> contacts,
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

            if (!slot.InUse || slot.Grid is not null || !_filter.Matches(slot.Tag)
                || IsIgnored(_ignore, index, slot.Generation))
            {
                return true;
            }

            if (SeparationOf(_shape, slot.World, out Vector2 normal, out Vector2 point) > _tolerance)
            {
                return true;
            }

            _contacts[Count++] = new Contact(
                CollisionTarget.ForCollider(_world.HandleAt(index), slot.Tag),
                point,
                normal);

            return true;
        }
    }

    private ref struct CastVisitor : ITreeVisitor
    {
        private readonly CollisionWorld _world;
        private readonly Shape _moving;
        private readonly Vector2 _translation;
        private readonly CollisionFilter _filter;
        private readonly ColliderHandle _ignore;
        private readonly Span<Contact> _contacts;

        internal CastVisitor(
            CollisionWorld world,
            in Shape moving,
            Vector2 translation,
            CollisionFilter filter,
            ColliderHandle ignore,
            Span<Contact> contacts,
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

        internal CastAccumulator Accumulator { get; private set; }

        public bool Visit(int proxyId)
        {
            int index = _world._tree.UserDataOf(proxyId);
            ref ColliderSlot slot = ref _world._slots[index];

            if (!slot.InUse || slot.Grid is not null || !_filter.Matches(slot.Tag)
                || IsIgnored(_ignore, index, slot.Generation))
            {
                return true;
            }

            if (!SweepAgainst(_moving, _translation, slot.World, out float fraction, out Vector2 normal, out Vector2 point))
            {
                return true;
            }

            CastAccumulator accumulator = Accumulator;
            Consider(
                ref accumulator,
                _contacts,
                _translation,
                fraction,
                CollisionTarget.ForCollider(_world.HandleAt(index), slot.Tag),
                normal,
                point);
            Accumulator = accumulator;

            return true;
        }
    }
}
