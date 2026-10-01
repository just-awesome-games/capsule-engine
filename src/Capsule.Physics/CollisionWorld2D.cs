using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Physics.Internal;

namespace Capsule.Physics;

/// <summary>
/// Every collider a game can hit, and the queries and sweeps that ask about them.
/// </summary>
/// <remarks>
/// A scene's colliders and tile maps register themselves in its world. A world detects collision
/// only. It has no dynamics and no solver. Shape colliders sit in a bounding-volume tree, and
/// terrain sits in <see cref="GridCollider2D"/> grids. A world is single-threaded, and no query
/// allocates once its colliders exist.
/// <para>
/// A world accepts only the handles, layers and filters it issued. Every query takes the filter it
/// matches by. A collider's stored filter does not decide what a query finds.
/// <see cref="CollisionFilter.None"/> and <see cref="CollisionFilter.Everything"/> name no table
/// and are accepted anywhere.
/// </para>
/// </remarks>
public sealed partial class CollisionWorld2D
{
    /// <summary>
    /// The name of the layer every world interns at creation, at index 0.
    /// </summary>
    public const string DefaultLayerName = "default";

    /// <summary>How many distinct layers one world may intern.</summary>
    public const int MaxLayers = 64;

    // Worlds are numbered from one. A default handle or layer carries world zero and belongs to no
    // world. The interlock covers test hosts that build worlds on several threads.
    private static int WorldsCreated;

    private readonly int _id = Interlocked.Increment(ref WorldsCreated);
    private readonly Dictionary<string, int> _layerIndices = new(StringComparer.Ordinal);
    private readonly List<string> _layerNames = [];
    private readonly List<GridCollider2D> _grids = [];
    private readonly List<int> _freeSlots = [];
    private readonly DynamicTree2D _tree = new();

    // The slots whose proxy was inserted or reinserted since contacts last settled, each listed once.
    private readonly List<int> _moved = [];

    // Every layer any contact filter has named, and every layer a collider with a contact filter has
    // been on. Both only grow, which keeps them supersets for the pair query's mask.
    private ulong _detected;
    private ulong _detectors;

    private ColliderSlot[] _slots = new ColliderSlot[16];
    private int _slotsUsed;

    // What each CollisionMask resolved to here, indexed by the mask's id. A resolved entry stays
    // right because interned layers never move.
    private ResolvedMask[] _masks = [];

    // How many masks this world's table has room for.
    internal int MaskTableLength => _masks.Length;

    // The slot a MovePast sweep passes through, or -1. Only one sweep runs at a time.
    private int _passThrough = -1;

    // The running sweep's hits within FractionBand of its nearest so far, in the order they arrived.
    // Each sweep starts it empty. A band that outgrows it is written by a second walk instead.
    private readonly BandHit[] _band = new BandHit[32];
    private int _bandCount;

    // A world holding nothing, with only DefaultLayerName interned.
    internal CollisionWorld2D() => Layer(DefaultLayerName);

    // How many colliders and grid colliders the world holds.
    internal int ColliderCount { get; private set; }

    // How many distinct layers have been interned, DefaultLayerName included.
    internal int LayerCount => _layerIndices.Count;

    // The grid colliders the world holds, in the order they were added.
    internal ReadOnlySpan<GridCollider2D> Grids => CollectionsMarshal.AsSpan(_grids);

    // How many grid cells this world's queries have reached since the last ResetDiagnostics, empty
    // ones included. No query result depends on it.
    internal long GridCellsTested { get; private set; }

    /// <summary>
    /// Declares the layer <paramref name="name"/>, interning it the first time it is seen. The same
    /// registration order yields the same indices.
    /// </summary>
    /// <exception cref="InvalidOperationException">The world already holds <see cref="MaxLayers"/> layers.</exception>
    public CollisionLayer Layer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_layerIndices.TryGetValue(name, out int index))
        {
            return new CollisionLayer(_id, index);
        }

        if (_layerNames.Count == MaxLayers)
        {
            throw new InvalidOperationException($"The world already holds its {MaxLayers} layers, so '{name}' cannot be added. Reuse an existing layer.");
        }

        CollisionLayer layer = new(_id, _layerNames.Count);
        _layerIndices.Add(name, _layerNames.Count);
        _layerNames.Add(name);

        return layer;
    }

    /// <summary>The layer <paramref name="name"/> was declared as, without declaring it.</summary>
    /// <exception cref="ArgumentException">No layer of that name has been declared.</exception>
    public CollisionLayer FindLayer(string name) =>
        TryFindLayer(name, out CollisionLayer layer)
            ? layer
            : throw new ArgumentException($"No collision layer is named '{name}'. Declare it with Layer first.", nameof(name));

    /// <summary>Looks up the layer <paramref name="name"/> was interned under, without interning it.</summary>
    /// <returns>
    /// Whether the name was interned. <paramref name="layer"/> is that layer, or the default value,
    /// which belongs to no world and every filter rejects.
    /// </returns>
    public bool TryFindLayer(string name, out CollisionLayer layer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_layerIndices.TryGetValue(name, out int index))
        {
            layer = new CollisionLayer(_id, index);
            return true;
        }

        layer = default;

        return false;
    }

    /// <summary>The name <paramref name="layer"/> was interned under.</summary>
    /// <exception cref="ArgumentException">The layer was interned by no world, or by another one.</exception>
    public string NameOf(CollisionLayer layer)
    {
        RequireOwn(layer);
        ArgumentOutOfRangeException.ThrowIfNegative(layer.Index, nameof(layer));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(layer.Index, _layerNames.Count, nameof(layer));

        return _layerNames[layer.Index];
    }

    /// <summary>A filter matching every layer in <paramref name="names"/>, each already declared.</summary>
    /// <returns>A filter of this world matching those layers. <see cref="CollisionFilter.None"/> for no names.</returns>
    /// <exception cref="ArgumentException">A name is null or blank, or names no declared layer.</exception>
    public CollisionFilter CreateFilter(params ReadOnlySpan<string> names)
    {
        CollisionFilter filter = CollisionFilter.None;
        foreach (string name in names)
        {
            filter = filter.With(FindLayer(name));
        }

        return filter;
    }

    // Adds a collider at position, with shape held in the collider's own space. UserDataOf returns
    // userData.
    internal ColliderHandle Add(
        in Shape2D shape,
        Vector2 position,
        CollisionLayer layer,
        object? userData = null)
    {
        RequireShape(shape, nameof(shape));
        Guard.Finite(position, nameof(position));
        RequireOwn(layer);

        // Placed before the slot is claimed. A shape that cannot be positioned leaves the world
        // unchanged.
        Shape2D placed = shape.Translated(position);

        int index = AllocateSlot();
        ref ColliderSlot slot = ref _slots[index];
        slot.Local = shape;
        slot.Position = position;
        slot.World = placed;
        slot.Layer = layer;
        slot.UserData = userData;
        slot.Grid = null;
        slot.OneWay = false;
        slot.SolidSides = false;
        slot.ProxyId = _tree.CreateProxy(slot.World.Bounds, index, CollisionFilter.Of(layer).Bits);
        NoteMoved(index);

        return HandleAt(index);
    }

    // Removes a collider. A handle to it reads as absent afterwards.
    internal void Remove(ColliderHandle handle)
    {
        int index = RequireSlot(handle);
        ref ColliderSlot slot = ref _slots[index];

        if (slot.Grid is { } grid)
        {
            _grids.Remove(grid);
        }
        else
        {
            SetContactFilter(handle, CollisionFilter.None);
            _tree.DestroyProxy(slot.ProxyId);
        }

        slot.InUse = false;
        slot.UserData = null;
        slot.Grid = null;
        _freeSlots.Add(index);
        ColliderCount--;
    }

    internal void Remove(GridCollider2D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Remove(grid.Handle);
    }

    // Whether the handle still names a live collider of this world.
    internal bool Contains(ColliderHandle handle)
    {
        RequireOwn(handle, nameof(handle));

        return TryIndexOf(handle, out _);
    }

    // Places a collider's shape origin at position without a sweep.
    internal void SetPosition(ColliderHandle handle, Vector2 position)
    {
        Guard.Finite(position, nameof(position));

        int index = RequireShapeSlot(handle);
        ref ColliderSlot slot = ref _slots[index];

        if (slot.Position == position)
        {
            return;
        }

        // Translated before the slot is written. A rejected position leaves the collider where it was,
        // with no stale proxy.
        Shape2D placed = slot.Local.Translated(position);
        Vector2 displacement = position - slot.Position;
        Guard.Finite(displacement, nameof(position));

        slot.Position = position;
        slot.World = placed;
        if (_tree.MoveProxy(slot.ProxyId, placed.Bounds, displacement))
        {
            NoteMoved(index);
        }
    }

    // Replaces a collider's shape, keeping its position.
    internal void SetShape(ColliderHandle handle, in Shape2D shape)
    {
        RequireShape(shape, nameof(shape));

        int index = RequireShapeSlot(handle);
        ref ColliderSlot slot = ref _slots[index];

        // Same order as SetPosition. Nothing is written until the placed shape is known good.
        Shape2D placed = shape.Translated(slot.Position);
        slot.Local = shape;
        slot.World = placed;
        if (_tree.MoveProxy(slot.ProxyId, placed.Bounds, Vector2.Zero))
        {
            NoteMoved(index);
        }
    }

    // Sets whether a collider blocks only a mover landing on it from above.
    internal void SetOneWay(ColliderHandle handle, bool oneWay) => _slots[RequireShapeSlot(handle)].OneWay = oneWay;

    // Sets whether a one-way collider also blocks from the sides.
    internal void SetSolidSides(ColliderHandle handle, bool solidSides) => _slots[RequireShapeSlot(handle)].SolidSides = solidSides;

    // Whether a target blocks only from above, as a one-way collider or cell without solid sides does.
    // No query meets its sides.
    internal bool IsTopOnly(in CollisionTarget target)
    {
        ref ColliderSlot slot = ref _slots[RequireSlot(target.Collider)];
        if (slot.Grid is { } grid)
        {
            return (grid.StateAt(target.CellX, target.CellY) & (CellState2D.OneWay | CellState2D.SolidSides)) == CellState2D.OneWay;
        }

        return slot.OneWay && !slot.SolidSides;
    }

    // Replaces the layer a collider is on.
    internal void SetLayer(ColliderHandle handle, CollisionLayer layer)
    {
        RequireOwn(layer);

        // A grid has no single layer to write. Its cells carry the layers their profiles named, and
        // tile queries read those.
        int index = RequireShapeSlot(handle);
        _slots[index].Layer = layer;
        _tree.SetProxyMask(_slots[index].ProxyId, CollisionFilter.Of(layer).Bits);
        if (!_slots[index].ContactFilter.IsEmpty)
        {
            _detectors |= CollisionFilter.Of(layer).Bits;
        }

        NoteMoved(index);
    }

    // Where a collider's shape origin sits.
    internal Vector2 PositionOf(ColliderHandle handle) => _slots[RequireShapeSlot(handle)].Position;

    // A collider's shape in world space, its local shape translated by its position.
    internal Shape2D WorldShapeOf(ColliderHandle handle) => _slots[RequireShapeSlot(handle)].World;

    /// <summary>A collider's shape, in its own space.</summary>
    /// <exception cref="ArgumentException">The handle names no live collider, or names a grid.</exception>
    public Shape2D ShapeOf(ColliderHandle handle) => _slots[RequireShapeSlot(handle)].Local;

    /// <summary>
    /// The layer a collider is on. A grid's cells carry the layers of the profiles they were painted
    /// from, which <see cref="GridCollider2D.LayerAt"/> reads.
    /// </summary>
    /// <exception cref="ArgumentException">The handle names no live collider, or names a grid.</exception>
    public CollisionLayer LayerOf(ColliderHandle handle) => _slots[RequireShapeSlot(handle)].Layer;

    // Whatever the caller attached to a collider or grid when it was added. Throws for a handle that
    // names nothing live.
    internal object? UserDataOf(ColliderHandle handle) => _slots[RequireSlot(handle)].UserData;

    /// <summary>
    /// The grid collider a handle names, or null when it names a shape collider or nothing live. The
    /// per-collider accessors describe a single shape and refuse a grid's handle.
    /// </summary>
    public GridCollider2D? GridOf(ColliderHandle handle)
    {
        RequireOwn(handle, nameof(handle));

        return TryIndexOf(handle, out int index) ? _slots[index].Grid : null;
    }

    // Whatever was attached to a live collider or grid, or null when the handle names nothing live.
    internal object? UserDataOrNull(ColliderHandle handle)
    {
        RequireOwn(handle, nameof(handle));

        return TryIndexOf(handle, out int index) ? _slots[index].UserData : null;
    }

    // Adds a grid of collidable cells anchored at the world origin. The cell array is copied.
    internal GridCollider2D AddGrid(
        int cellSize,
        int width,
        int height,
        int[] cells,
        ReadOnlySpan<CellProfile2D> profiles,
        object? userData = null)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"Grid size {width}x{height} is empty. Use at least one cell on each axis.", nameof(width));
        }

        // Widened because an int product wraps, and a wrapped area would let a mismatched cell array
        // through.
        long area = (long)width * height;
        if (cells.Length != area)
        {
            throw new ArgumentException($"cells has {cells.Length} entries but {width}x{height} requires {area}.", nameof(cells));
        }

        if (profiles.Length == 0)
        {
            throw new ArgumentException("profiles is empty. Supply at least one profile for the cells to index.", nameof(profiles));
        }

        for (int index = 0; index < profiles.Length; index++)
        {
            CellProfile2D profile = profiles[index];

            if (profile.Layer is { } layer)
            {
                RequireOwn(layer);
            }
            else if (profile.Shape is not null || profile.OneWay)
            {
                throw new ArgumentException(
                    $"profiles[{index}] declares a shape or one-way but no layer. Give it a layer, or drop both.",
                    nameof(profiles));
            }

            if (profile.SolidSides && !profile.OneWay)
            {
                throw new ArgumentException(
                    $"profiles[{index}] declares solid sides but is not one-way. Make it one-way, or drop solid sides.",
                    nameof(profiles));
            }

            if (profile.Shape is { } shape)
            {
                RequireCellShape(shape, cellSize, index);
            }
        }

        for (int index = 0; index < cells.Length; index++)
        {
            if ((uint)cells[index] >= (uint)profiles.Length)
            {
                throw new ArgumentException(
                    $"cells[{index}] is {cells[index]}, which is not a profile index (0..{profiles.Length - 1}).",
                    nameof(cells));
            }
        }

        int slotIndex = AllocateSlot();
        ref ColliderSlot slot = ref _slots[slotIndex];
        slot.ProxyId = DynamicTree2D.NullNode;
        slot.Layer = Layer(DefaultLayerName);
        slot.UserData = userData;

        GridCollider2D grid = new(
            HandleAt(slotIndex),
            cellSize,
            width,
            height,
            (int[])cells.Clone(),
            profiles);

        slot.Grid = grid;
        _grids.Add(grid);

        return grid;
    }

    /// <summary>
    /// The first thing a ray from <paramref name="origin"/> meets within <paramref name="distance"/>
    /// world units, passing through <paramref name="ignore"/>, typically the caster's own collider.
    /// Any non-zero <paramref name="direction"/> works.
    /// </summary>
    /// <returns>Whether the ray met anything. <paramref name="hit"/> is the nearest when it did.</returns>
    /// <exception cref="ArgumentException"><paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public bool Raycast(
        Vector2 origin,
        Vector2 direction,
        float distance,
        CollisionFilter filter,
        out RayHit2D hit,
        ColliderHandle ignore = default)
    {
        Vector2 unit = RequireRay(origin, direction, distance);
        RequireOwn(filter, nameof(filter));
        RequireIgnorable(ignore);

        return RaycastWalk(origin, unit, distance, filter, ignore, out hit);
    }

    // The nearest-hit ray walk, for a caller that has validated every argument.
    private bool RaycastWalk(Vector2 origin, Vector2 unit, float distance, CollisionFilter filter, ColliderHandle ignore, out RayHit2D hit)
    {
        hit = default;

        RayAccumulator accumulator = new() { Distance = distance };
        int count = 0;
        RaycastGrids(origin, unit, filter, ignore, ref accumulator, default, ref count);
        RaycastColliders(origin, unit, filter, ignore, ref accumulator, default, ref count);

        if (!accumulator.Hit)
        {
            return false;
        }

        hit = new RayHit2D(accumulator.Target, origin + (unit * accumulator.Distance), accumulator.Normal, accumulator.Distance);

        return true;
    }

    /// <summary>
    /// The nearest things a ray meets, cast as
    /// <see cref="Raycast(Vector2, Vector2, float, CollisionFilter, out RayHit2D, ColliderHandle)"/>
    /// casts it and written into <paramref name="hits"/> nearest first. A span of <c>n</c> receives
    /// the <c>n</c> nearest hits.
    /// </summary>
    /// <returns>How many hits were written, at most the length of <paramref name="hits"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public int RaycastAll(
        Vector2 origin,
        Vector2 direction,
        float distance,
        CollisionFilter filter,
        Span<RayHit2D> hits,
        ColliderHandle ignore = default)
    {
        Vector2 unit = RequireRay(origin, direction, distance);
        RequireOwn(filter, nameof(filter));
        RequireIgnorable(ignore);

        return RaycastAllWalk(origin, unit, distance, filter, hits, ignore);
    }

    // The all-hits ray walk, for a caller that has validated every argument.
    private int RaycastAllWalk(Vector2 origin, Vector2 unit, float distance, CollisionFilter filter, Span<RayHit2D> hits, ColliderHandle ignore)
    {
        if (hits.IsEmpty)
        {
            return 0;
        }

        RayAccumulator accumulator = new() { Distance = distance };
        int count = 0;
        RaycastGrids(origin, unit, filter, ignore, ref accumulator, hits, ref count);
        RaycastColliders(origin, unit, filter, ignore, ref accumulator, hits, ref count);

        return count;
    }

    /// <summary>
    /// Where <paramref name="shape"/>, held in its own space and starting at <paramref name="origin"/>,
    /// first meets something when swept <paramref name="translation"/> world units, passing through
    /// <paramref name="ignore"/>. A shape already touching something reports it at fraction 0 when the
    /// sweep drives into it, and passes it by when the sweep moves away.
    /// </summary>
    /// <returns>Whether the sweep met anything. <paramref name="hit"/> is the nearest when it did.</returns>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, or <paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public bool ShapeCast(
        in Shape2D shape,
        Vector2 origin,
        Vector2 translation,
        CollisionFilter filter,
        out ShapeCastHit2D hit,
        ColliderHandle ignore = default)
    {
        RequireShape(shape, nameof(shape));
        Guard.Finite(origin, nameof(origin));
        Guard.Finite(translation, nameof(translation));
        RequireOwn(filter, nameof(filter));
        RequireIgnorable(ignore);

        return ShapeCastSweep(shape, origin, translation, filter, ignore, out hit);
    }

    // The sweep, for a caller that has validated every argument.
    private bool ShapeCastSweep(in Shape2D shape, Vector2 origin, Vector2 translation, CollisionFilter filter, ColliderHandle ignore, out ShapeCastHit2D hit)
    {
        hit = default;
        Shape2D moving = shape.Translated(origin);
        CastAccumulator accumulator = default;
        Cast(moving, translation, filter, ignore, default, false, ref accumulator);

        if (!accumulator.Hit)
        {
            return false;
        }

        hit = new ShapeCastHit2D(accumulator.Target, accumulator.Point, accumulator.Normal, accumulator.Fraction);

        return true;
    }

    /// <summary>
    /// Everything a shape at <paramref name="origin"/> is inside or touching. Grid cells come
    /// first, in the order their grids were added and then row-major within each.
    /// </summary>
    /// <remarks>Colliders follow by handle.</remarks>
    /// <returns>
    /// How many overlaps there were. The span holds as many as fit, in that order, and the rest are
    /// counted only.
    /// </returns>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, or <paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public int OverlapAll(
        in Shape2D shape,
        Vector2 origin,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default)
    {
        RequireShape(shape, nameof(shape));
        Guard.Finite(origin, nameof(origin));
        RequireOwn(filter, nameof(filter));
        RequireIgnorable(ignore);

        return FindContacts(shape.Translated(origin), filter, 0f, ignore, contacts);
    }

    /// <summary>
    /// Everything an axis-aligned box, already placed, is inside or touching. The box form of
    /// <see cref="OverlapAll(in Shape2D, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/>.
    /// </summary>
    /// <returns>How many overlaps there were, of which the span holds the first.</returns>
    /// <exception cref="ArgumentException">The box is one <see cref="Shape2D.Box(in Aabb2D)"/> refuses, or <paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public int OverlapBoxAll(
        in Aabb2D box,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default) =>
        OverlapAll(Shape2D.Box(box), Vector2.Zero, filter, contacts, ignore);

    /// <summary>
    /// Everything that contains <paramref name="point"/> or has it on an edge. The point form of
    /// <see cref="OverlapAll(in Shape2D, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/>.
    /// </summary>
    /// <returns>How many overlaps there were, of which the span holds the first.</returns>
    /// <exception cref="ArgumentException"><paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public int OverlapPointAll(
        Vector2 point,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default)
    {
        Guard.Finite(point, nameof(point));
        RequireOwn(filter, nameof(filter));
        RequireIgnorable(ignore);

        return FindContacts(Shape2D.OfPoint(point), filter, 0f, ignore, contacts);
    }

    /// <summary>
    /// Everything a registered collider is touching, meaning anything within
    /// <see cref="CollisionTolerance.ContactSkin"/> of it that matches <paramref name="filter"/>.
    /// </summary>
    /// <remarks>
    /// The collider itself is excluded. Ordering matches
    /// <see cref="OverlapAll(in Shape2D, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/>.
    /// </remarks>
    /// <returns>How many overlaps there were, of which the span holds the first.</returns>
    /// <exception cref="ArgumentException">The handle names no live collider, or names a grid, or the filter belongs to another world.</exception>
    public int OverlapColliderAll(ColliderHandle handle, CollisionFilter filter, Span<Contact2D> contacts)
    {
        RequireOwn(filter, nameof(filter));

        int index = RequireShapeSlot(handle);

        return FindContacts(_slots[index].World, filter, CollisionTolerance.ContactSkin, handle, contacts);
    }

    // Whether two of this world's colliders are within the contact skin of each other, and where the
    // contact on the second one is. Collider2D.Overlaps wraps this and carries the documented contract.
    internal bool OverlapPair(ColliderHandle collider, ColliderHandle other, CollisionFilter filter, out Contact2D contact)
    {
        RequireOwn(filter, nameof(filter));

        int index = RequireShapeSlot(collider);
        int otherIndex = RequireShapeSlot(other);
        contact = default;

        ref ColliderSlot target = ref _slots[otherIndex];
        if (index == otherIndex || !filter.Admits(target.Layer))
        {
            return false;
        }

        // The same measurement the overlap walk makes. A pair test and an overlap query describe a
        // contact identically.
        float separation = Separation(_slots[index].World, target.World, out Vector2 normal, out Vector2 point);
        if (separation > CollisionTolerance.ContactSkin)
        {
            return false;
        }

        contact = new Contact2D(CollisionTarget.ForCollider(other, target.Layer), point, normal, DepthOf(separation));

        return true;
    }

    // Sets the filter a collider's settled contacts detect, which is empty for one that reports none.
    // Its candidates are gathered afresh at the next settle.
    internal void SetContactFilter(ColliderHandle handle, CollisionFilter filter)
    {
        int index = RequireShapeSlot(handle);
        _slots[index].ContactFilter = filter;
        _slots[index].CandidateCount = 0;
        if (!filter.IsEmpty)
        {
            _detected |= filter.Bits;
            _detectors |= CollisionFilter.Of(_slots[index].Layer).Bits;
        }

        NoteMoved(index);
    }

    // What OverlapColliderAll finds under the collider's contact filter, in the same order. The
    // narrowphase runs over the grids and the collider's candidates only. A candidate that is stale,
    // filtered out, or whose fat box has left reach drops out of the list.
    internal int ContactsOf(ColliderHandle handle, Span<Contact2D> contacts)
    {
        if (_moved.Count > 0)
        {
            FindNewPairs();
        }

        int index = RequireShapeSlot(handle);
        ref ColliderSlot slot = ref _slots[index];
        CollisionFilter filter = slot.ContactFilter;
        int written = FindGridContacts(slot.World, filter, CollisionTolerance.ContactSkin, handle, contacts, out int found);
        Aabb2D reach = _tree.FatBoxOf(slot.ProxyId).Expanded(CollisionTolerance.ContactSkin);

        int kept = 0;
        for (int candidate = 0; candidate < slot.CandidateCount; candidate++)
        {
            ColliderHandle other = slot.Candidates![candidate];
            if (!TryIndexOf(other, out int otherIndex))
            {
                continue;
            }

            ref ColliderSlot target = ref _slots[otherIndex];
            if (!filter.Admits(target.Layer) || !reach.Overlaps(_tree.FatBoxOf(target.ProxyId)))
            {
                continue;
            }

            slot.Candidates[kept++] = other;
            float separation = Separation(slot.World, target.World, out Vector2 normal, out Vector2 point);
            if (separation > CollisionTolerance.ContactSkin)
            {
                continue;
            }

            found++;
            if (written < contacts.Length)
            {
                contacts[written++] = new Contact2D(CollisionTarget.ForCollider(other, target.Layer), point, normal, DepthOf(separation));
            }
        }

        slot.CandidateCount = kept;

        return found;
    }

    // Orders two targets the way an overlap query writes them. A cell of a grid that has left the
    // world sorts ahead of every other target.
    internal int OverlapOrder(in CollisionTarget left, in CollisionTarget right)
    {
        if (left.IsGridCell != right.IsGridCell)
        {
            return left.IsGridCell ? -1 : 1;
        }

        if (!left.IsGridCell)
        {
            return left.Collider.Index.CompareTo(right.Collider.Index);
        }

        if (left.Collider != right.Collider)
        {
            return GridRank(left.Collider).CompareTo(GridRank(right.Collider));
        }

        return left.CellY != right.CellY ? left.CellY.CompareTo(right.CellY) : left.CellX.CompareTo(right.CellX);
    }

    // A grid's place in the order grids were added, or -1 once it has left the world.
    private int GridRank(ColliderHandle grid)
    {
        for (int rank = 0; rank < _grids.Count; rank++)
        {
            if (_grids[rank].Handle == grid)
            {
                return rank;
            }
        }

        return -1;
    }

    private void NoteMoved(int index)
    {
        ref ColliderSlot slot = ref _slots[index];
        if (!slot.Moved)
        {
            slot.Moved = true;
            _moved.Add(index);
        }
    }

    // Pairs each moved proxy with every collider near it, making each side a candidate of the other
    // where the other's contact filter admits it. Tight bounds stay inside fat ones, so two shapes
    // within the skin have fat boxes within the skin too. The query reaches twice that and finds a
    // pair ContactsOf keeps whichever side moved, rounding included. It visits only the layers the
    // moved collider detects, and the layers of colliders that may detect it.
    private void FindNewPairs()
    {
        foreach (int index in _moved)
        {
            ref ColliderSlot slot = ref _slots[index];
            slot.Moved = false;
            if (!slot.InUse || slot.Grid is not null)
            {
                continue;
            }

            ulong mask = slot.ContactFilter.Bits | ((_detected & CollisionFilter.Of(slot.Layer).Bits) != 0 ? _detectors : 0);
            PairVisitor visitor = new(this, index);
            _tree.Query(_tree.FatBoxOf(slot.ProxyId).Expanded(2f * CollisionTolerance.ContactSkin), mask, ref visitor);
        }

        _moved.Clear();
    }

    private void Pair(int first, int second)
    {
        if (_slots[first].ContactFilter.Admits(_slots[second].Layer))
        {
            AddCandidate(ref _slots[first], HandleAt(second));
        }

        if (_slots[second].ContactFilter.Admits(_slots[first].Layer))
        {
            AddCandidate(ref _slots[second], HandleAt(first));
        }
    }

    // Keeps the list in slot order with one entry per slot. A handle to a reused slot replaces the
    // stale one.
    private static void AddCandidate(ref ColliderSlot slot, ColliderHandle handle)
    {
        slot.Candidates ??= new ColliderHandle[8];
        int count = slot.CandidateCount;
        int position = count;
        while (position > 0 && slot.Candidates[position - 1].Index > handle.Index)
        {
            position--;
        }

        if (position > 0 && slot.Candidates[position - 1].Index == handle.Index)
        {
            slot.Candidates[position - 1] = handle;
            return;
        }

        if (count == slot.Candidates.Length)
        {
            Array.Resize(ref slot.Candidates, count * 2);
        }

        Array.Copy(slot.Candidates, position, slot.Candidates, position + 1, count - position);
        slot.Candidates[position] = handle;
        slot.CandidateCount = count + 1;
    }

    /// <summary>
    /// Moves <paramref name="shape"/>, held in its own space and starting at <paramref name="origin"/>,
    /// as far along <paramref name="translation"/> world units as it can go.
    /// </summary>
    /// <remarks>
    /// The move sweeps to the first surface, stops a slop short of it, and slides what is left along
    /// that surface, in at most four sweeps. It tunnels through nothing at any speed. It skips
    /// <paramref name="ignore"/>. <paramref name="contacts"/> receives the surfaces reached, pass by
    /// pass. A pass reaches the nearest surface and every other within a ten-thousandth of the
    /// translation of it. Within each pass, grid cells come in traversal order and then colliders by
    /// handle. The span may be empty. Nothing in the world moves. The caller adds
    /// <see cref="MoveResult2D.Translation"/> to its own position.
    /// </remarks>
    /// <returns>How far the shape actually moved, whether anything stopped it, and how many surfaces it reached.</returns>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, or <paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public MoveResult2D Move(
        in Shape2D shape,
        Vector2 origin,
        Vector2 translation,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default)
    {
        RequireShape(shape, nameof(shape));
        Guard.Finite(origin, nameof(origin));
        Guard.Finite(translation, nameof(translation));

        // The move runs from the origin to this point, so every position between is finite once both
        // ends are.
        Guard.Finite(origin + translation, nameof(translation));
        RequireOwn(filter, nameof(filter));
        RequireIgnorable(ignore);

        return Slide(shape, origin, translation, filter, contacts, ignore);
    }

    /// <summary>
    /// Moves an axis-aligned box, already placed. The box form of
    /// <see cref="Move(in Shape2D, Vector2, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/>.
    /// </summary>
    /// <returns>How far the box actually moved, whether anything stopped it, and how many surfaces it reached.</returns>
    /// <exception cref="ArgumentException">The box is one <see cref="Shape2D.Box(in Aabb2D)"/> refuses, or <paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public MoveResult2D MoveBox(
        in Aabb2D box,
        Vector2 translation,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default) =>
        Move(Shape2D.Box(box), Vector2.Zero, translation, filter, contacts, ignore);

    // Zeroes GridCellsTested and changes nothing else.
    internal void ResetDiagnostics() => GridCellsTested = 0;

    private static Vector2 RequireRay(Vector2 origin, Vector2 direction, float distance)
    {
        Guard.Finite(origin, nameof(origin));
        Guard.Finite(distance, nameof(distance));
        ArgumentOutOfRangeException.ThrowIfNegative(distance);

        float length = direction.Length();
        if (!float.IsFinite(length) || length <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Direction is zero or not finite. Pass a non-zero finite direction.");
        }

        return direction / length;
    }

    // A grid cell's shape is a plain convex polygon inside its cell.
    private static void RequireCellShape(in Shape2D shape, int cellSize, int index)
    {
        if (shape.Kind is not (ShapeKind2D.Polygon or ShapeKind2D.Box) || shape.Radius != 0f)
        {
            throw new ArgumentException(
                $"profiles[{index}] has a {shape.Kind} shape of radius {shape.Radius}. Use a polygon with no radius.",
                "profiles");
        }

        if (shape.Bounds.Min.X < 0f || shape.Bounds.Min.Y < 0f || shape.Bounds.Max.X > cellSize || shape.Bounds.Max.Y > cellSize)
        {
            throw new ArgumentException(
                $"profiles[{index}] has a shape reaching outside its {cellSize}-unit cell. Keep every point within [0, {cellSize}].",
                "profiles");
        }
    }

    private static void RequireShape(in Shape2D shape, string parameterName)
    {
        if (shape.PointCount == 0)
        {
            throw new ArgumentException(
                "Shape is a default Shape2D. Build one with Shape2D.Box, Circle, Capsule or Polygon.",
                parameterName);
        }
    }

    private ColliderHandle HandleAt(int index) => new(_id, index, _slots[index].Generation);

    private void RequireOwn(ColliderHandle handle, string parameterName)
    {
        if (!handle.IsNone && handle.World != _id)
        {
            throw new ArgumentException("The handle was issued by another collision world.", parameterName);
        }
    }

    // None means no ignore and always passes. Any other handle must name a live collider, since a
    // removed collider's index may since have been reissued.
    private void RequireIgnorable(ColliderHandle ignore)
    {
        RequireOwn(ignore, nameof(ignore));

        if (!ignore.IsNone && !TryIndexOf(ignore, out _))
        {
            throw new ArgumentException(
                "Handle names no collider in this world. It was never added, or it has been removed.",
                nameof(ignore));
        }
    }

    private void RequireOwn(CollisionLayer layer)
    {
        if (layer.World != _id)
        {
            throw new ArgumentException("The layer was interned by another collision world, or by none.", nameof(layer));
        }
    }

    // None and Everything index no table, so they pass everywhere.
    private void RequireOwn(CollisionFilter filter, string parameterName)
    {
        if (filter.World != 0 && filter.World != _id)
        {
            throw new ArgumentException("The filter was built from another collision world's layers.", parameterName);
        }
    }

    private int AllocateSlot()
    {
        int index;
        if (_freeSlots.Count > 0)
        {
            index = _freeSlots[^1];
            _freeSlots.RemoveAt(_freeSlots.Count - 1);
        }
        else
        {
            if (_slotsUsed == _slots.Length)
            {
                Array.Resize(ref _slots, _slots.Length * 2);
            }

            index = _slotsUsed++;
        }

        ref ColliderSlot slot = ref _slots[index];
        slot.Generation++;
        slot.InUse = true;
        ColliderCount++;

        return index;
    }

    private bool TryIndexOf(ColliderHandle handle, out int index)
    {
        index = handle.Index;

        return !handle.IsNone
            && (uint)index < (uint)_slotsUsed
            && _slots[index].InUse
            && _slots[index].Generation == handle.Generation;
    }

    private int RequireSlot(ColliderHandle handle)
    {
        RequireOwn(handle, nameof(handle));

        return TryIndexOf(handle, out int index)
            ? index
            : throw new ArgumentException(
                "Handle names no collider in this world. It was never added, or it has been removed.",
                nameof(handle));
    }

    private int RequireShapeSlot(ColliderHandle handle)
    {
        int index = RequireSlot(handle);

        return _slots[index].Grid is null
            ? index
            : throw new ArgumentException("Handle names a grid collider, which has no single shape or position.", nameof(handle));
    }

    private struct ColliderSlot
    {
        internal Shape2D Local;
        internal Shape2D World;
        internal Vector2 Position;
        internal CollisionLayer Layer;
        internal object? UserData;
        internal GridCollider2D? Grid;
        internal int ProxyId;
        internal int Generation;
        internal bool InUse;
        internal bool OneWay;
        internal bool SolidSides;

        // What this collider's settled contacts detect, and the colliders whose fat boxes are within
        // the skin of its own, sorted by slot. Moved marks the slot as listed in _moved.
        internal CollisionFilter ContactFilter;
        internal ColliderHandle[]? Candidates;
        internal int CandidateCount;
        internal bool Moved;
    }

    private readonly struct PairVisitor(CollisionWorld2D world, int index) : ITreeVisitor2D
    {
        public bool Visit(int proxyId)
        {
            int other = world._tree.UserDataOf(proxyId);
            if (other != index)
            {
                world.Pair(index, other);
            }

            return true;
        }
    }

    private struct ResolvedMask
    {
        internal CollisionFilter Filter;
        internal bool Resolved;
    }

    private struct RayAccumulator
    {
        internal float Distance;
        internal Vector2 Normal;
        internal CollisionTarget Target;
        internal bool Hit;
    }

    private struct CastAccumulator
    {
        // The translation's length, and how far below zero a normal's dot with the translation must
        // reach for the sweep to drive into it. Both are set once per cast.
        internal float Length;
        internal float Lean;

        // The nearest hit, which is the band's primary.
        internal float Fraction;
        internal Vector2 Normal;
        internal Vector2 Point;
        internal CollisionTarget Target;
        internal bool Hit;

        // How many contacts the band holds, how many of them the caller's span had room for, and
        // where in the span the colliders after the last grid cell begin.
        internal int Found;
        internal int Written;
        internal int FirstCollider;

        // Whether the band outgrew the world's scratch, and whether this is the second walk, which
        // knows the primary and writes the band as it goes.
        internal bool Overflowed;
        internal bool Anchored;
    }

    private readonly struct BandHit(float fraction, Contact2D contact)
    {
        internal readonly float Fraction = fraction;
        internal readonly Contact2D Contact = contact;
    }
}
