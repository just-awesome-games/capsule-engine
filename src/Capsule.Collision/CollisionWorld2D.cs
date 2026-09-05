using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Collision.Internal;

namespace Capsule.Collision;

/// <summary>
/// Every collider a game can hit, and the only way to ask about them. Collision only: shapes,
/// broadphase, queries and sweeps, with no dynamics and no solver. Moving colliders sit in a
/// dynamic bounding-volume hierarchy; terrain sits in <see cref="GridCollider2D"/> grids that are
/// their own broadphase. A world is single-threaded, and every query is allocation-free once its
/// colliders exist.
/// <para>
/// The handles, layers and filters a world hands out are its own; another world's are rejected with
/// <see cref="ArgumentException"/>. <see cref="CollisionFilter.None"/> and
/// <see cref="CollisionFilter.Everything"/> name no table and are accepted anywhere.
/// </para>
/// </summary>
public sealed partial class CollisionWorld2D
{
    /// <summary>
    /// The layer a collider is on until it is told otherwise. Interned at world creation, so it is
    /// always the first entry of the table and things collide by default.
    /// </summary>
    public const string DefaultLayerName = "default";

    /// <summary>How many distinct layers one world may intern.</summary>
    public const int MaxLayers = 64;

    /// <summary>
    /// World units of tolerance the whole module works to: the gap a blocked move keeps from what
    /// stopped it, and the slack that stops a flush face from reading as an overlap.
    /// </summary>
    public const float LinearSlop = 0.005f;

    /// <summary>
    /// World units within which a collider counts as touching another. Wider than
    /// <see cref="LinearSlop"/>, so something a move came to rest against still reports contact on
    /// the following step.
    /// </summary>
    public const float ContactSkin = 0.02f;

    // Numbered from one, so a default handle or layer — world zero — belongs to none of them. The
    // interlock is for test hosts that build worlds on several threads.
    private static int WorldsCreated;

    private readonly int _id = Interlocked.Increment(ref WorldsCreated);
    private readonly Dictionary<string, int> _layerIndices = new(StringComparer.Ordinal);
    private readonly List<string> _layerNames = [];
    private readonly List<GridCollider2D> _grids = [];
    private readonly List<int> _freeSlots = [];
    private readonly DynamicTree _tree = new();

    // How many tree-resident colliders stand on each layer, and the same as a mask. A grid carries
    // its own Layers and every grid walk already skips a grid no filter bit reaches; this is that
    // early-out for the tree, which otherwise descends to a leaf before the layer is ever read.
    private readonly int[] _treeLayerCounts = new int[MaxLayers];

    private ColliderSlot[] _slots = new ColliderSlot[16];
    private int _slotsUsed;
    private CollisionFilter _treeLayers;

    /// <summary>A world holding nothing, with only <see cref="DefaultLayerName"/> interned.</summary>
    public CollisionWorld2D() => Layer(DefaultLayerName);

    /// <summary>How many colliders and grid colliders the world holds.</summary>
    public int ColliderCount { get; private set; }

    /// <summary>How many distinct layers have been interned, <see cref="DefaultLayerName"/> included.</summary>
    public int LayerCount => _layerIndices.Count;

    // The grid colliders the world holds, in the order they were added.
    internal ReadOnlySpan<GridCollider2D> Grids => CollectionsMarshal.AsSpan(_grids);

    // How many grid cells this world's casts and rays have reached since the last
    // ResetDiagnostics, empty ones included; overlaps are not counted. No query result depends
    // on it.
    internal long GridCellsTested { get; private set; }

    /// <summary>
    /// The layer <paramref name="name"/> interns to, interning it the first time it is seen. The
    /// same registration order always yields the same indices.
    /// </summary>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
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
            throw new InvalidOperationException(
                $"A collision world interns at most {MaxLayers} layers and already holds that many, so '{name}' has nowhere to go; collision filtering is meant to name a handful of kinds, not every type in the game.");
        }

        CollisionLayer layer = new(_id, _layerNames.Count);
        _layerIndices.Add(name, _layerNames.Count);
        _layerNames.Add(name);

        return layer;
    }

    /// <summary>The layer <paramref name="name"/> was interned under, without interning it.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
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
    /// <exception cref="ArgumentOutOfRangeException">The layer is past what this world has interned.</exception>
    public string NameOf(CollisionLayer layer)
    {
        RequireOwn(layer);
        ArgumentOutOfRangeException.ThrowIfNegative(layer.Index, nameof(layer));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(layer.Index, _layerNames.Count, nameof(layer));

        return _layerNames[layer.Index];
    }

    /// <summary>A filter naming every layer in <paramref name="names"/>, interning any that is new.</summary>
    /// <returns>A filter of this world matching exactly those layers; <see cref="CollisionFilter.None"/> for no names.</returns>
    /// <exception cref="InvalidOperationException">Interning would exceed <see cref="MaxLayers"/>.</exception>
    public CollisionFilter CreateFilter(params ReadOnlySpan<string> names)
    {
        CollisionFilter filter = CollisionFilter.None;
        foreach (string name in names)
        {
            filter = filter.With(Layer(name));
        }

        return filter;
    }

    /// <summary>Adds a collider at <paramref name="position"/> and returns its handle.</summary>
    /// <param name="shape">The shape, in the collider's own space; <paramref name="position"/> places it.</param>
    /// <param name="position">Where the shape's origin sits in the world.</param>
    /// <param name="layer">The layer this collider is on, for other queries' filters to match.</param>
    /// <param name="detects">What this collider's own moves and contact queries may hit.</param>
    /// <param name="userData">Anything the caller wants to find its way back from a query result.</param>
    /// <exception cref="ArgumentException">
    /// The shape is a default <see cref="Shape2D"/>, the layer or the filter came from another
    /// world, or the shape placed at this position exceeds what a float box holds.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    public ColliderHandle Add(
        in Shape2D shape,
        Vector2 position,
        CollisionLayer layer,
        CollisionFilter detects,
        object? userData = null)
    {
        RequireShape(shape, nameof(shape));
        RequireFinite(position, nameof(position));
        RequireOwn(layer);
        RequireOwn(detects, nameof(detects));

        // Placed before the slot is claimed, so a shape that cannot be positioned leaves the world
        // as it was rather than half-registered.
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
        slot.ProxyId = _tree.CreateProxy(slot.World.Bounds, index);
        TrackTreeLayer(layer, 1);

        return HandleAt(index);
    }

    /// <summary>Removes a collider; a handle to it reads as absent afterwards.</summary>
    /// <exception cref="ArgumentException">The handle names no live collider, or was issued by another world.</exception>
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
            TrackTreeLayer(slot.Layer, -1);
        }

        slot.InUse = false;
        slot.UserData = null;
        slot.Grid = null;
        _freeSlots.Add(index);
        ColliderCount--;
    }

    /// <summary>Removes a grid collider.</summary>
    /// <exception cref="ArgumentException">The grid belongs to no world, or to another one.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public void Remove(GridCollider2D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        Remove(grid.Handle);
    }

    // Whether <paramref name="handle"/> still names a live collider of this world.
    internal bool Contains(ColliderHandle handle)
    {
        RequireOwn(handle, nameof(handle));

        return TryIndexOf(handle, out _);
    }

    /// <summary>Moves a collider, refitting its broadphase entry.</summary>
    /// <exception cref="ArgumentException">
    /// The handle names no live collider, names a grid, was issued by another world, or the
    /// shape placed at this position exceeds what a float box holds.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The position is not finite, or the step from where the collider stands to it is not — two
    /// positions at opposite ends of the float range have an infinite one between them.
    /// </exception>
    public void SetPosition(ColliderHandle handle, Vector2 position)
    {
        RequireFinite(position, nameof(position));

        int index = RequireShapeSlot(handle);
        ref ColliderSlot slot = ref _slots[index];

        if (slot.Position == position)
        {
            return;
        }

        // Translated before the slot is written, so a rejected position leaves the collider where it
        // was rather than half-moved with a stale proxy. The displacement is derived, and two finite
        // positions at opposite ends of the range have an infinite one between them.
        Shape2D placed = slot.Local.Translated(position);
        Vector2 displacement = position - slot.Position;
        RequireFinite(displacement, nameof(position));

        slot.Position = position;
        slot.World = placed;
        _tree.MoveProxy(slot.ProxyId, placed.Bounds, displacement);
    }

    /// <summary>Replaces a collider's shape, keeping its position.</summary>
    /// <exception cref="ArgumentException">
    /// The handle names no live collider, names a grid, or was issued by another world; or the
    /// shape is a default <see cref="Shape2D"/>, or exceeds what a float box holds where the collider
    /// stands.
    /// </exception>
    public void SetShape(ColliderHandle handle, in Shape2D shape)
    {
        RequireShape(shape, nameof(shape));

        int index = RequireShapeSlot(handle);
        ref ColliderSlot slot = ref _slots[index];

        // Same order as SetPosition: nothing is written until the placed shape is known good.
        Shape2D placed = shape.Translated(slot.Position);
        slot.Local = shape;
        slot.World = placed;
        _tree.MoveProxy(slot.ProxyId, placed.Bounds, Vector2.Zero);
    }

    /// <summary>Replaces the layer a collider is on and what it may hit.</summary>
    /// <exception cref="ArgumentException">
    /// The handle names no live collider, names a grid, or was issued by another world; or the
    /// layer or the filter came from another world.
    /// </exception>
    public void SetFilter(ColliderHandle handle, CollisionLayer layer, CollisionFilter detects)
    {
        RequireOwn(layer);
        RequireOwn(detects, nameof(detects));

        // A grid has no one layer to write: its cells carry the layers their profiles named, and
        // every tile query reads those rather than the slot's.
        int index = RequireShapeSlot(handle);
        TrackTreeLayer(_slots[index].Layer, -1);
        TrackTreeLayer(layer, 1);
        _slots[index].Layer = layer;
        _slots[index].Detects = detects;
    }

    // Where a collider's shape origin sits.
    internal Vector2 PositionOf(ColliderHandle handle) => _slots[RequireShapeSlot(handle)].Position;

    /// <summary>A collider's shape, in its own space.</summary>
    /// <exception cref="ArgumentException">The handle names no live collider, names a grid, or was issued by another world.</exception>
    public Shape2D ShapeOf(ColliderHandle handle) => _slots[RequireShapeSlot(handle)].Local;

    /// <summary>
    /// The layer a collider is on. A grid's cells carry the layers of the profiles they were
    /// painted from, which <see cref="GridCollider2D.LayerAt"/> reads.
    /// </summary>
    /// <exception cref="ArgumentException">The handle names no live collider, names a grid, or was issued by another world.</exception>
    public CollisionLayer LayerOf(ColliderHandle handle) => _slots[RequireShapeSlot(handle)].Layer;

    // What a collider may hit. A grid collider never moves and hits nothing.
    internal CollisionFilter FilterOf(ColliderHandle handle) => _slots[RequireShapeSlot(handle)].Detects;

    /// <summary>Whatever the caller attached to a collider or grid when it was added.</summary>
    /// <exception cref="ArgumentException">The handle names no live collider of this world.</exception>
    public object? UserDataOf(ColliderHandle handle) => _slots[RequireSlot(handle)].UserData;

    /// <summary>
    /// The grid collider a handle names, or null when it names a shape collider. The per-collider
    /// accessors describe a single shape and refuse a grid's handle.
    /// </summary>
    /// <exception cref="ArgumentException">The handle was issued by another world.</exception>
    public GridCollider2D? GridOf(ColliderHandle handle)
    {
        RequireOwn(handle, nameof(handle));

        return TryIndexOf(handle, out int index) ? _slots[index].Grid : null;
    }

    /// <summary>
    /// Adds a grid of collidable cells anchored at the world origin. The cell array is held rather
    /// than copied and its faces are derived once, so repainting cells afterwards requires a new
    /// collider.
    /// </summary>
    /// <param name="cellSize">World units a cell spans on each axis.</param>
    /// <param name="width">Cells across.</param>
    /// <param name="height">Cells down.</param>
    /// <param name="cells">One <paramref name="profiles"/> index per cell, row-major.</param>
    /// <param name="profiles">The layer each palette entry's cells are on, and which of their sides collide.</param>
    /// <param name="userData">Anything the caller wants to find its way back from a query result.</param>
    /// <exception cref="ArgumentException">Some invariant of the grid is broken; the message names the defect.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="cells"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cellSize"/> is not positive.</exception>
    public GridCollider2D AddGrid(
        int cellSize,
        int width,
        int height,
        int[] cells,
        ReadOnlySpan<CellProfile2D> profiles,
        object? userData = null)
    {
        ArgumentNullException.ThrowIfNull(cells);

        if (cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "A grid collider's cell size must be positive.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"A grid collider is at least one cell on each axis, not {width}x{height}.", nameof(width));
        }

        // Widened: an int product wraps, and a wrapped area would let a mismatched cell array
        // through.
        long area = (long)width * height;
        if (cells.Length != area)
        {
            throw new ArgumentException(
                $"cells has {cells.Length} entries but {width}x{height} requires {area}.",
                nameof(cells));
        }

        if (profiles.Length == 0)
        {
            throw new ArgumentException("A grid collider needs at least one profile for its cells to index.", nameof(profiles));
        }

        CollisionLayer?[] layers = new CollisionLayer?[profiles.Length];
        CellFaces2D[] faces = new CellFaces2D[profiles.Length];
        for (int index = 0; index < profiles.Length; index++)
        {
            CellProfile2D profile = profiles[index];

            if ((profile.Faces & ~CellFaces2D.All) != 0)
            {
                throw new ArgumentException(
                    $"profiles[{index}] declares faces {(int)profile.Faces}, which is not a combination of the four sides a cell has.",
                    nameof(profiles));
            }

            if (profile.Layer is { } layer)
            {
                RequireOwn(layer);

                // A layer with no face is a cell nothing can meet: an authoring mistake rather than
                // a way to spell an empty cell.
                if (profile.Faces == CellFaces2D.None)
                {
                    throw new ArgumentException(
                        $"profiles[{index}] is on a layer but declares no faces; a cell that collides needs at least one side, and one that collides as nothing is written with no layer.",
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
        slot.ProxyId = DynamicTree.NullNode;
        slot.Detects = CollisionFilter.None;
        slot.Layer = Layer(DefaultLayerName);
        slot.UserData = userData;

        GridCollider2D grid = new(
            HandleAt(slotIndex),
            cellSize,
            width,
            height,
            cells,
            layers,
            faces);

        slot.Grid = grid;
        _grids.Add(grid);

        return grid;
    }

    /// <summary>The first thing a ray meets.</summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="direction">Which way it points; normalised here, so any non-zero length will do.</param>
    /// <param name="distance">How far along <paramref name="direction"/> to look, in world units.</param>
    /// <param name="filter">What the ray may hit.</param>
    /// <param name="hit">The nearest hit, when there is one.</param>
    /// <param name="ignore">A collider the ray passes through, typically the caster's own.</param>
    /// <returns>Whether the ray met anything.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The direction is zero or not finite, or the distance is negative or not finite.</exception>
    /// <exception cref="ArgumentException"><paramref name="filter"/> was built from another world's layers, or <paramref name="ignore"/> is not <see cref="ColliderHandle.None"/> and names no live collider of this world.</exception>
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
    /// The nearest things a ray meets, written into <paramref name="hits"/> in ascending distance.
    /// The span is the budget as well as the destination: a span of <c>n</c> receives the <c>n</c>
    /// nearest hits, and a nearer hit displaces the farthest already stored.
    /// </summary>
    /// <param name="origin">Where the ray starts, in world units.</param>
    /// <param name="direction">Which way it points; normalised here, so any non-zero length will do.</param>
    /// <param name="distance">How far along <paramref name="direction"/> to look, in world units.</param>
    /// <param name="filter">What the ray may hit.</param>
    /// <param name="hits">Where the nearest hits are written, nearest first.</param>
    /// <param name="ignore">A collider the ray passes through, typically the caster's own.</param>
    /// <returns>How many hits were written, never more than <paramref name="hits"/> holds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The direction is zero or not finite, or the distance is negative or not finite.</exception>
    /// <exception cref="ArgumentException"><paramref name="filter"/> was built from another world's layers, or <paramref name="ignore"/> is not <see cref="ColliderHandle.None"/> and names no live collider of this world.</exception>
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
    /// Where a shape swept along <paramref name="translation"/> first meets something. A shape
    /// already touching something reports it at fraction 0 when the sweep drives into it, and
    /// passes it by when the sweep moves away — there is no solver to push out of a penetration.
    /// </summary>
    /// <param name="shape">The shape to sweep, in its own space.</param>
    /// <param name="origin">Where that shape starts.</param>
    /// <param name="translation">How far and which way to sweep it, in world units.</param>
    /// <param name="filter">What the sweep may hit.</param>
    /// <param name="hit">The nearest hit, when there is one.</param>
    /// <param name="ignore">A collider the sweep passes through, typically the sweeper's own.</param>
    /// <returns>Whether the sweep met anything.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The origin or the translation is not finite, or the box the sweep covers between them is not.
    /// </exception>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, the filter came from another world, or <paramref name="ignore"/> is not <see cref="ColliderHandle.None"/> and names no live collider of this world.</exception>
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
        RequireFinite(origin, nameof(origin));
        RequireFinite(translation, nameof(translation));
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
    /// Everything a shape at <paramref name="origin"/> is inside or touching, written into
    /// <paramref name="contacts"/>. Grid cells come first, in the order their grids were added and
    /// then row-major within each; colliders follow, in the order they were added.
    /// </summary>
    /// <returns>How many contacts were written, never more than <paramref name="contacts"/> holds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The origin is not finite.</exception>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, the filter came from another world, or <paramref name="ignore"/> is not <see cref="ColliderHandle.None"/> and names no live collider of this world.</exception>
    public int OverlapAll(
        in Shape2D shape,
        Vector2 origin,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default)
    {
        RequireShape(shape, nameof(shape));
        RequireFinite(origin, nameof(origin));
        RequireOwn(filter, nameof(filter));
        RequireIgnorable(ignore);

        return Touching(shape.Translated(origin), filter, 0f, ignore, contacts);
    }

    /// <summary>
    /// Everything an axis-aligned box, already placed, is inside or touching; the box form of
    /// <see cref="OverlapAll(in Shape2D, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/>.
    /// </summary>
    /// <returns>How many contacts were written, never more than <paramref name="contacts"/> holds.</returns>
    /// <exception cref="ArgumentException">The box spans nothing on an axis, the filter came from another world, or <paramref name="ignore"/> is not <see cref="ColliderHandle.None"/> and names no live collider of this world.</exception>
    public int OverlapBoxAll(
        in Aabb2D box,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default) =>
        OverlapAll(Shape2D.Box(box), Vector2.Zero, filter, contacts, ignore);

    /// <summary>
    /// Everything a registered collider is touching right now — within <see cref="ContactSkin"/>
    /// of it, matching its own filter, and never itself. Ordering matches
    /// <see cref="OverlapAll(in Shape2D, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/>.
    /// </summary>
    /// <returns>How many contacts were written, never more than <paramref name="contacts"/> holds.</returns>
    /// <exception cref="ArgumentException">The handle names no live collider, names a grid, or was issued by another world.</exception>
    public int OverlapColliderAll(ColliderHandle handle, Span<Contact2D> contacts)
    {
        int index = RequireShapeSlot(handle);

        return Touching(_slots[index].World, _slots[index].Detects, ContactSkin, handle, contacts);
    }

    // Whether two of this world's colliders are within ContactSkin of each other, and where the
    // contact on the second one is. Layers and filters play no part: the caller named the pair.
    // Collider2D.Overlaps is the public face of this, and carries the documented contract.
    internal bool OverlapPair(ColliderHandle collider, ColliderHandle other, out Contact2D contact)
    {
        int index = RequireShapeSlot(collider);
        int otherIndex = RequireShapeSlot(other);
        contact = default;

        if (index == otherIndex)
        {
            return false;
        }

        // The same measurement TouchVisitor makes, so a pair test and an overlap query describe
        // one contact identically.
        ref ColliderSlot target = ref _slots[otherIndex];
        if (SeparationOf(_slots[index].World, target.World, out Vector2 normal, out Vector2 point) > ContactSkin)
        {
            return false;
        }

        contact = new Contact2D(CollisionTarget.ForCollider(other, target.Layer), point, normal);

        return true;
    }

    /// <summary>
    /// Moves a shape as far along <paramref name="translation"/> as it can go, one axis at a time:
    /// X first to its first contact, then Y from there, so stopping on one axis never stops the
    /// other. The move is swept, so nothing is passed through at any speed. Contacts come X sweep
    /// first and then Y; within each, grid cells in traversal order and then colliders by handle.
    /// </summary>
    /// <param name="shape">The shape to move, in its own space.</param>
    /// <param name="origin">Where that shape starts.</param>
    /// <param name="translation">How far and which way to move the shape, in world units.</param>
    /// <param name="filter">What may block the move.</param>
    /// <param name="contacts">Where the surfaces the sweep reached are written; may be empty.</param>
    /// <param name="ignore">A collider the move passes through, typically the mover's own.</param>
    /// <returns>How far the shape actually moved, which axes were blocked, and how many contacts were written.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The origin or the translation is not finite, or the box the sweep covers between them is not.
    /// </exception>
    /// <exception cref="ArgumentException">The shape is a default <see cref="Shape2D"/>, the filter came from another world, or <paramref name="ignore"/> is not <see cref="ColliderHandle.None"/> and names no live collider of this world.</exception>
    public MoveResult2D Move(
        in Shape2D shape,
        Vector2 origin,
        Vector2 translation,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default)
    {
        RequireShape(shape, nameof(shape));
        RequireFinite(origin, nameof(origin));
        RequireFinite(translation, nameof(translation));

        // One axis at a time from the origin to this point, so every position between is finite
        // once both ends are.
        RequireFinite(origin + translation, nameof(translation));
        RequireOwn(filter, nameof(filter));
        RequireIgnorable(ignore);

        Vector2 at = origin;
        Vector2 applied = Vector2.Zero;
        int written = 0;

        bool blockedX = SweepAxis(shape, ref at, translation.X, true, filter, ignore, contacts, ref written, out float movedX);
        applied.X = movedX;
        int xContacts = written;

        bool blockedY = SweepAxis(shape, ref at, translation.Y, false, filter, ignore, contacts, ref written, out float movedY);
        applied.Y = movedY;

        return new MoveResult2D(applied, blockedX, blockedY, written, xContacts);
    }

    /// <summary>
    /// Moves an axis-aligned box, already placed; the box form of
    /// <see cref="Move(in Shape2D, Vector2, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/>.
    /// </summary>
    /// <returns>How far the box actually moved, which axes were blocked, and how many contacts were written.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The translation is not finite, or the box the move covers between its ends is not.
    /// </exception>
    /// <exception cref="ArgumentException">The box spans nothing on an axis, the filter came from another world, or <paramref name="ignore"/> is not <see cref="ColliderHandle.None"/> and names no live collider of this world.</exception>
    public MoveResult2D MoveBox(
        in Aabb2D box,
        Vector2 translation,
        CollisionFilter filter,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default) =>
        Move(Shape2D.Box(box), Vector2.Zero, translation, filter, contacts, ignore);

    // Zeroes GridCellsTested, leaving everything the world holds alone.
    internal void ResetDiagnostics() => GridCellsTested = 0;

    private static Vector2 RequireRay(Vector2 origin, Vector2 direction, float distance)
    {
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "A ray's origin must be finite.");
        }

        if (!float.IsFinite(distance) || distance < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(distance), distance, "A ray's distance must be finite and non-negative.");
        }

        float length = direction.Length();
        if (!float.IsFinite(length) || length <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "A ray's direction must be finite and longer than nothing.");
        }

        return direction / length;
    }

    // A NaN or infinite bound does not stay in its own proxy: the tree unions boxes as it balances,
    // so one poisoned proxy spreads through the ancestors it shares with unrelated colliders and
    // makes them unfindable. Checked at the seam, which costs a pair of tests per call and none per
    // node or per cell.
    private static void RequireFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be finite; an infinite or NaN one would spread through the broadphase and lose colliders that have nothing to do with it.");
        }
    }

    private static void RequireFinite(in Aabb2D bounds, string parameterName)
    {
        RequireFinite(bounds.Min, parameterName);
        RequireFinite(bounds.Max, parameterName);
    }

    private static void RequireShape(in Shape2D shape, string parameterName)
    {
        if (shape.PointCount == 0)
        {
            throw new ArgumentException(
                "A default Shape2D holds no points and is not a shape; build one with Shape2D.Box, Shape2D.Circle, Shape2D.Capsule or Shape2D.Polygon.",
                parameterName);
        }
    }

    private ColliderHandle HandleAt(int index) => new(_id, index, _slots[index].Generation);

    private void RequireOwn(ColliderHandle handle, string parameterName)
    {
        if (!handle.IsNone && handle.World != _id)
        {
            throw new ArgumentException(
                "The handle was issued by another collision world; a handle names a slot, and that slot means nothing here.",
                parameterName);
        }
    }

    // None is the no-ignore value and always passes; anything else must name a live collider,
    // because a handle to a removed one carries an index since handed to somebody unrelated.
    private void RequireIgnorable(ColliderHandle ignore)
    {
        RequireOwn(ignore, nameof(ignore));

        if (!ignore.IsNone && !TryIndexOf(ignore, out _))
        {
            throw new ArgumentException(
                "The handle names no collider this world holds, so there is nothing for the query to ignore; it was never added, or it has been removed and its slot given to something else.",
                nameof(ignore));
        }
    }

    private void RequireOwn(CollisionLayer layer)
    {
        if (layer.World != _id)
        {
            throw new ArgumentException(
                "The layer was interned by another collision world, or by none; a layer is an index into one world's table and means nothing in another.",
                nameof(layer));
        }
    }

    // None and Everything index no table, so they pass everywhere.
    private void RequireOwn(CollisionFilter filter, string parameterName)
    {
        if (filter.World != 0 && filter.World != _id)
        {
            throw new ArgumentException(
                "The filter was built from another collision world's layers; its bits index that world's table, not this one's.",
                parameterName);
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

    // A layer leaves the mask only when the last collider on it leaves the tree, so the mask is
    // exact rather than monotonic: a world that once held an actor does not go on paying for one.
    private void TrackTreeLayer(CollisionLayer layer, int delta)
    {
        int count = _treeLayerCounts[layer.Index] += delta;

        if (delta > 0 && count == 1)
        {
            _treeLayers = _treeLayers.With(layer);
        }
        else if (delta < 0 && count == 0)
        {
            _treeLayers = _treeLayers.Without(layer);
        }
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
            : throw new ArgumentException("The handle names no collider this world holds; it was never added, or it has been removed.", nameof(handle));
    }

    private int RequireShapeSlot(ColliderHandle handle)
    {
        int index = RequireSlot(handle);

        return _slots[index].Grid is null
            ? index
            : throw new ArgumentException("The handle names a grid collider, which has no single shape or position.", nameof(handle));
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
        // Where the contact band opened; Fraction is the primary hit inside it.
        internal float Band;
        internal float Fraction;
        internal Vector2 Normal;
        internal Vector2 Point;
        internal CollisionTarget Target;
        internal bool Hit;
        internal int Count;
    }
}
