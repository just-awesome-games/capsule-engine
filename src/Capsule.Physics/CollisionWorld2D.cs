using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Physics.Internal;

namespace Capsule.Physics;

/// <summary>
/// Every collider a game can hit, and the API for asking about them. Collision only, meaning shapes,
/// broadphase, queries and sweeps, with no dynamics and no solver. Moving colliders sit in a dynamic
/// bounding-volume hierarchy, and terrain sits in <see cref="GridCollider2D"/> grids that are their
/// own broadphase. A world is single-threaded, and every query is allocation-free once its colliders
/// exist.
/// <para>
/// A world accepts only the handles, layers and filters it issued. Every query takes the filter it
/// matches by. A collider's stored filter does not decide what a query finds.
/// <see cref="CollisionFilter.None"/> and <see cref="CollisionFilter.Everything"/> name no table and
/// are accepted anywhere.
/// </para>
/// </summary>
public sealed partial class CollisionWorld2D
{
    /// <summary>
    /// The layer a collider is on unless told otherwise. Interned at world creation, so it is the
    /// first entry of the table.
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

    private ColliderSlot[] _slots = new ColliderSlot[16];
    private int _slotsUsed;

    // The slot a MovePast sweep passes through, or -1. Only one sweep runs at a time.
    private int _passThrough = -1;

    /// <summary>A world holding nothing, with only <see cref="DefaultLayerName"/> interned.</summary>
    public CollisionWorld2D() => Layer(DefaultLayerName);

    /// <summary>How many colliders and grid colliders the world holds.</summary>
    public int ColliderCount { get; private set; }

    /// <summary>How many distinct layers have been interned, <see cref="DefaultLayerName"/> included.</summary>
    public int LayerCount => _layerIndices.Count;

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

    /// <summary>
    /// Adds a collider whose <paramref name="shape"/> is held in the collider's own space with its
    /// origin at <paramref name="position"/>. <paramref name="detects"/> is the default filter for the
    /// collider's own queries, and <paramref name="userData"/> is anything the caller wants back from
    /// a query result.
    /// </summary>
    /// <returns>The handle naming the new collider.</returns>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, or placed at this position it exceeds what a float box holds, or the layer or filter belongs to another world.</exception>
    public ColliderHandle Add(
        in Shape2D shape,
        Vector2 position,
        CollisionLayer layer,
        CollisionFilter detects,
        object? userData = null)
    {
        RequireShape(shape, nameof(shape));
        Guard.Finite(position, nameof(position));
        RequireOwn(layer);
        RequireOwn(detects, nameof(detects));

        // Placed before the slot is claimed. A shape that cannot be positioned leaves the world
        // unchanged.
        Shape2D placed = shape.Translated(position);

        int index = AllocateSlot();
        ref ColliderSlot slot = ref _slots[index];
        slot.Local = shape;
        slot.Position = position;
        slot.World = placed;
        slot.Layer = layer;
        slot.Detects = detects;
        slot.UserData = userData;
        slot.Grid = null;
        slot.ProxyId = _tree.CreateProxy(slot.World.Bounds, index, CollisionFilter.Of(layer).Bits);

        return HandleAt(index);
    }

    /// <summary>Removes a collider. A handle to it reads as absent afterwards.</summary>
    /// <exception cref="ArgumentException">The handle names no live collider.</exception>
    public void Remove(ColliderHandle handle)
    {
        int index = RequireSlot(handle);
        ref ColliderSlot slot = ref _slots[index];

        if (slot.Grid is { } grid)
        {
            _grids.Remove(grid);
        }
        else
        {
            _tree.DestroyProxy(slot.ProxyId);
        }

        slot.InUse = false;
        slot.UserData = null;
        slot.Grid = null;
        _freeSlots.Add(index);
        ColliderCount--;
    }

    /// <summary>Removes a grid collider.</summary>
    /// <exception cref="ArgumentException">The grid belongs to no world, or to another one.</exception>
    public void Remove(GridCollider2D grid)
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

    /// <summary>Moves a collider, refitting its broadphase entry.</summary>
    /// <exception cref="ArgumentException">The handle names no live collider or names a grid, or the shape placed at this position exceeds what a float box holds.</exception>
    public void SetPosition(ColliderHandle handle, Vector2 position)
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
        _tree.MoveProxy(slot.ProxyId, placed.Bounds, displacement);
    }

    /// <summary>Replaces a collider's shape, keeping its position.</summary>
    /// <exception cref="ArgumentException">The handle names no live collider or names a grid, or the shape is a default <see cref="Shape2D"/> or exceeds what a float box holds where the collider stands.</exception>
    public void SetShape(ColliderHandle handle, in Shape2D shape)
    {
        RequireShape(shape, nameof(shape));

        int index = RequireShapeSlot(handle);
        ref ColliderSlot slot = ref _slots[index];

        // Same order as SetPosition. Nothing is written until the placed shape is known good.
        Shape2D placed = shape.Translated(slot.Position);
        slot.Local = shape;
        slot.World = placed;
        _tree.MoveProxy(slot.ProxyId, placed.Bounds, Vector2.Zero);
    }

    /// <summary>Replaces the layer a collider is on and the filter its own queries take by default.</summary>
    /// <exception cref="ArgumentException">The handle names no live collider, or names a grid, or the layer or filter belongs to another world.</exception>
    public void SetFilter(ColliderHandle handle, CollisionLayer layer, CollisionFilter detects)
    {
        RequireOwn(layer);
        RequireOwn(detects, nameof(detects));

        // A grid has no single layer to write. Its cells carry the layers their profiles named, and
        // tile queries read those.
        int index = RequireShapeSlot(handle);
        _slots[index].Layer = layer;
        _slots[index].Detects = detects;
        _tree.SetProxyMask(_slots[index].ProxyId, CollisionFilter.Of(layer).Bits);
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

    // The filter a collider's own queries take when none is given. A grid collider never moves and
    // hits nothing.
    internal CollisionFilter FilterOf(ColliderHandle handle) => _slots[RequireShapeSlot(handle)].Detects;

    /// <summary>Whatever the caller attached to a collider or grid when it was added.</summary>
    /// <exception cref="ArgumentException">The handle names no live collider.</exception>
    public object? UserDataOf(ColliderHandle handle) => _slots[RequireSlot(handle)].UserData;

    /// <summary>
    /// The grid collider a handle names, or null when it names a shape collider or nothing live. The
    /// per-collider accessors describe a single shape and refuse a grid's handle.
    /// </summary>
    public GridCollider2D? GridOf(ColliderHandle handle)
    {
        RequireOwn(handle, nameof(handle));

        return TryIndexOf(handle, out int index) ? _slots[index].Grid : null;
    }

    /// <summary>Adds a grid of collidable cells anchored at the world origin. The cell array is copied.</summary>
    /// <param name="cellSize">World units a cell spans on each axis.</param>
    /// <param name="width">Cells across.</param>
    /// <param name="height">Cells down.</param>
    /// <param name="cells">One <paramref name="profiles"/> index per cell, row-major.</param>
    /// <param name="profiles">The layer each palette entry's cells are on, and which of their sides collide.</param>
    /// <param name="userData">Anything the caller wants back from a query result.</param>
    /// <exception cref="ArgumentException">A grid invariant is broken. The message names the defect.</exception>
    public GridCollider2D AddGrid(
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

        CollisionLayer?[] layers = new CollisionLayer?[profiles.Length];
        CellFaces2D[] faces = new CellFaces2D[profiles.Length];
        for (int index = 0; index < profiles.Length; index++)
        {
            CellProfile2D profile = profiles[index];

            if ((profile.Faces & ~CellFaces2D.All) != 0)
            {
                throw new ArgumentException(
                    $"profiles[{index}] declares faces {(int)profile.Faces}, which is not a combination of the four cell sides.",
                    nameof(profiles));
            }

            if (profile.Layer is { } layer)
            {
                RequireOwn(layer);

                if (profile.Faces == CellFaces2D.None)
                {
                    throw new ArgumentException(
                        $"profiles[{index}] is on a layer but declares no faces. Use a null layer for a cell that collides as nothing.",
                        nameof(profiles));
                }

                layers[index] = layer;
            }

            faces[index] = profile.Faces;
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
        slot.Detects = CollisionFilter.None;
        slot.Layer = Layer(DefaultLayerName);
        slot.UserData = userData;

        GridCollider2D grid = new(
            HandleAt(slotIndex),
            cellSize,
            width,
            height,
            (int[])cells.Clone(),
            layers,
            faces);

        slot.Grid = grid;
        _grids.Add(grid);

        return grid;
    }

    /// <summary>
    /// The first thing a ray from <paramref name="origin"/> meets within <paramref name="distance"/>
    /// world units, passing through <paramref name="ignore"/>, typically the caster's own collider.
    /// <paramref name="direction"/> is normalised here, so any non-zero vector works.
    /// </summary>
    /// <returns>Whether the ray met anything. <paramref name="hit"/> is the nearest when it did.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The direction is zero or not finite, or the distance is negative or not finite.</exception>
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
    /// The nearest things a ray meets, cast as <see cref="Raycast"/> casts it and written into
    /// <paramref name="hits"/> nearest first. The span is the budget too. A span of <c>n</c> receives
    /// the <c>n</c> nearest hits, and a nearer hit displaces the farthest already stored.
    /// </summary>
    /// <returns>How many hits were written, at most the length of <paramref name="hits"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The direction is zero or not finite, or the distance is negative or not finite.</exception>
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
    /// <exception cref="ArgumentOutOfRangeException">The origin or the translation is not finite.</exception>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, or <paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public bool ShapeCast(
        in Shape2D shape,
        Vector2 origin,
        Vector2 translation,
        CollisionFilter filter,
        out ShapeCastHit2D hit,
        ColliderHandle ignore = default)
    {
        hit = default;
        RequireShape(shape, nameof(shape));
        Guard.Finite(origin, nameof(origin));
        Guard.Finite(translation, nameof(translation));
        RequireOwn(filter, nameof(filter));
        RequireIgnorable(ignore);

        Shape2D moving = shape.Translated(origin);
        CastAccumulator accumulator = default;
        Cast(moving, translation, filter, ignore, default, ref accumulator);

        if (!accumulator.Hit)
        {
            return false;
        }

        hit = new ShapeCastHit2D(accumulator.Target, accumulator.Point, accumulator.Normal, accumulator.Fraction);

        return true;
    }

    /// <summary>
    /// Everything a shape at <paramref name="origin"/> is inside or touching. Grid cells come first, in
    /// the order their grids were added and then row-major within each. Colliders follow by handle.
    /// </summary>
    /// <returns>
    /// How many overlaps there were. The span holds as many as fit, in that order, and the rest are
    /// counted only.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The origin is not finite.</exception>
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
    /// <exception cref="ArgumentException">The box spans nothing on an axis, or <paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
    public int OverlapBoxAll(
        in Aabb2D box,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default) =>
        OverlapAll(Shape2D.Box(box), Vector2.Zero, filter, contacts, ignore);

    /// <summary>
    /// Everything a registered collider is touching, meaning anything within
    /// <see cref="CollisionTolerance.ContactSkin"/> of it that matches <paramref name="filter"/>. The
    /// collider itself is excluded. Ordering matches
    /// <see cref="OverlapAll(in Shape2D, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/>.
    /// </summary>
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

    /// <summary>
    /// Moves <paramref name="shape"/>, held in its own space and starting at <paramref name="origin"/>,
    /// as far along <paramref name="translation"/> world units as it can go. The move runs one axis at
    /// a time, X to its first contact and then Y from there. A block on one axis leaves the other free.
    /// The move is swept, so nothing is passed through at any speed, and
    /// <paramref name="ignore"/> is skipped. Surfaces reached are written into
    /// <paramref name="contacts"/>, which may be empty, the X sweep's first and then the Y sweep's, and
    /// within each grid cells in traversal order and then colliders by handle.
    /// </summary>
    /// <returns>How far the shape actually moved, which axes were blocked, and how many surfaces it reached.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The origin, the translation, or the point they reach together is not finite.</exception>
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

        Vector2 at = origin;
        Vector2 applied = Vector2.Zero;
        int written = 0;
        int found = 0;

        bool blockedX = SweepAxis(shape, ref at, translation.X, true, filter, ignore, contacts, ref written, ref found, out float movedX);
        applied.X = movedX;
        int alongX = written;

        bool blockedY = SweepAxis(shape, ref at, translation.Y, false, filter, ignore, contacts, ref written, ref found, out float movedY);
        applied.Y = movedY;

        return new MoveResult2D(applied, blockedX, blockedY, found, alongX);
    }

    /// <summary>
    /// Moves an axis-aligned box, already placed. The box form of
    /// <see cref="Move(in Shape2D, Vector2, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/>.
    /// </summary>
    /// <returns>How far the box actually moved, which axes were blocked, and how many surfaces it reached.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The translation, or the point the move reaches, is not finite.</exception>
    /// <exception cref="ArgumentException">The box spans nothing on an axis, or <paramref name="ignore"/> names no live collider of this world, or the filter belongs to another one.</exception>
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
        internal CollisionFilter Detects;
        internal object? UserData;
        internal GridCollider2D? Grid;
        internal int ProxyId;
        internal int Generation;
        internal bool InUse;
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
        // Where the contact band opened. Fraction is the primary hit inside it.
        internal float Band;
        internal float Fraction;
        internal Vector2 Normal;
        internal Vector2 Point;
        internal CollisionTarget Target;
        internal bool Hit;

        // How many contacts the band holds, and how many of them the caller's span had room for.
        internal int Found;
        internal int Written;

        // Where the handle-ordered run of collider contacts starts, after the grid phase.
        internal int First;
    }
}
