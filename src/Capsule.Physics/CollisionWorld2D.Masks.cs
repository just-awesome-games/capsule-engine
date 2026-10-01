using System.Numerics;

namespace Capsule.Physics;

// The query overloads that take a CollisionMask, and the one path that turns layer names into a
// filter of this world.
public sealed partial class CollisionWorld2D
{
    /// <summary>
    /// The first thing a ray meets, matching by <paramref name="mask"/>. All other rules of
    /// <see cref="Raycast(Vector2, Vector2, float, CollisionFilter, out RayHit2D, ColliderHandle)"/> apply.
    /// </summary>
    /// <returns>Whether the ray met anything. <paramref name="hit"/> is the nearest when it did.</returns>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public bool Raycast(
        Vector2 origin,
        Vector2 direction,
        float distance,
        CollisionMask mask,
        out RayHit2D hit,
        ColliderHandle ignore = default)
    {
        Vector2 unit = RequireRay(origin, direction, distance);
        RequireIgnorable(ignore);

        return RaycastWalk(origin, unit, distance, Resolve(mask), ignore, out hit);
    }

    /// <summary>
    /// The nearest things a ray meets, matching by <paramref name="mask"/>. All other rules of
    /// <see cref="RaycastAll(Vector2, Vector2, float, CollisionFilter, Span{RayHit2D}, ColliderHandle)"/> apply.
    /// </summary>
    /// <returns>How many hits were written, at most the length of <paramref name="hits"/>.</returns>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public int RaycastAll(
        Vector2 origin,
        Vector2 direction,
        float distance,
        CollisionMask mask,
        Span<RayHit2D> hits,
        ColliderHandle ignore = default)
    {
        Vector2 unit = RequireRay(origin, direction, distance);
        RequireIgnorable(ignore);

        return RaycastAllWalk(origin, unit, distance, Resolve(mask), hits, ignore);
    }

    /// <summary>
    /// Where a swept shape first meets something, matching by <paramref name="mask"/>. All other
    /// rules of <see cref="ShapeCast(in Shape2D, Vector2, Vector2, CollisionFilter, out ShapeCastHit2D, ColliderHandle)"/> apply.
    /// </summary>
    /// <returns>Whether the sweep met anything. <paramref name="hit"/> is the nearest when it did.</returns>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public bool ShapeCast(
        in Shape2D shape,
        Vector2 origin,
        Vector2 translation,
        CollisionMask mask,
        out ShapeCastHit2D hit,
        ColliderHandle ignore = default)
    {
        RequireShape(shape, nameof(shape));
        Guard.Finite(origin, nameof(origin));
        Guard.Finite(translation, nameof(translation));
        RequireIgnorable(ignore);

        return ShapeCastSweep(shape, origin, translation, Resolve(mask), ignore, out hit);
    }

    /// <summary>
    /// Everything a shape is inside or touching, matching by <paramref name="mask"/>. All other rules
    /// of <see cref="OverlapAll(in Shape2D, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/> apply.
    /// </summary>
    /// <returns>How many overlaps there were, of which the span holds the first.</returns>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public int OverlapAll(
        in Shape2D shape,
        Vector2 origin,
        CollisionMask mask,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default)
    {
        RequireShape(shape, nameof(shape));
        Guard.Finite(origin, nameof(origin));
        RequireIgnorable(ignore);

        return FindContacts(shape.Translated(origin), Resolve(mask), 0f, ignore, contacts);
    }

    /// <summary>
    /// Everything a placed box is inside or touching, matching by <paramref name="mask"/>. All other
    /// rules of <see cref="OverlapBoxAll(in Aabb2D, CollisionFilter, Span{Contact2D}, ColliderHandle)"/> apply.
    /// </summary>
    /// <returns>How many overlaps there were, of which the span holds the first.</returns>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public int OverlapBoxAll(
        in Aabb2D box,
        CollisionMask mask,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default) =>
        OverlapAll(Shape2D.Box(box), Vector2.Zero, mask, contacts, ignore);

    /// <summary>
    /// Everything that contains <paramref name="point"/> or has it on an edge, matching by
    /// <paramref name="mask"/>. All other rules of
    /// <see cref="OverlapPointAll(Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/> apply.
    /// </summary>
    /// <returns>How many overlaps there were, of which the span holds the first.</returns>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public int OverlapPointAll(
        Vector2 point,
        CollisionMask mask,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default)
    {
        Guard.Finite(point, nameof(point));
        RequireIgnorable(ignore);

        return FindContacts(Shape2D.OfPoint(point), Resolve(mask), 0f, ignore, contacts);
    }

    /// <summary>
    /// Everything a registered collider is touching, matching by <paramref name="mask"/>. All other
    /// rules of <see cref="OverlapColliderAll(ColliderHandle, CollisionFilter, Span{Contact2D})"/> apply.
    /// </summary>
    /// <returns>How many overlaps there were, of which the span holds the first.</returns>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public int OverlapColliderAll(ColliderHandle handle, CollisionMask mask, Span<Contact2D> contacts)
    {
        int index = RequireShapeSlot(handle);

        return FindContacts(_slots[index].World, Resolve(mask), CollisionTolerance.ContactSkin, handle, contacts);
    }

    /// <summary>
    /// Moves a shape as far as it can go, stopped by what <paramref name="mask"/> matches. All other
    /// rules of <see cref="Move(in Shape2D, Vector2, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/> apply.
    /// </summary>
    /// <returns>How far the shape actually moved, whether anything stopped it, and how many surfaces it reached.</returns>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public MoveResult2D Move(
        in Shape2D shape,
        Vector2 origin,
        Vector2 translation,
        CollisionMask mask,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default)
    {
        RequireShape(shape, nameof(shape));
        Guard.Finite(origin, nameof(origin));
        Guard.Finite(translation, nameof(translation));
        Guard.Finite(origin + translation, nameof(translation));
        RequireIgnorable(ignore);

        return Slide(shape, origin, translation, Resolve(mask), contacts, ignore);
    }

    /// <summary>
    /// Moves a placed box as far as it can go, stopped by what <paramref name="mask"/> matches. All
    /// other rules of <see cref="MoveBox(in Aabb2D, Vector2, CollisionFilter, Span{Contact2D}, ColliderHandle)"/> apply.
    /// </summary>
    /// <returns>How far the box actually moved, whether anything stopped it, and how many surfaces it reached.</returns>
    /// <exception cref="InvalidOperationException">The world has no room left to intern a name of the mask.</exception>
    public MoveResult2D MoveBox(
        in Aabb2D box,
        Vector2 translation,
        CollisionMask mask,
        Span<Contact2D> contacts,
        ColliderHandle ignore = default) =>
        Move(Shape2D.Box(box), Vector2.Zero, translation, mask, contacts, ignore);

    // The filter a mask stands for in this world. The first call per mask interns its names and
    // stores the result. Later calls read it back by index. Every query validates its other arguments
    // first, so a refused query interns nothing.
    internal CollisionFilter Resolve(CollisionMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);

        int id = mask.Id;
        if (id < _masks.Length && _masks[id].Resolved)
        {
            return _masks[id].Filter;
        }

        // Intern before growing the table. A name the world has no room for leaves the table as it was.
        CollisionFilter filter = Intern(mask.Names);
        if (id >= _masks.Length)
        {
            Array.Resize(ref _masks, Math.Max(id + 1, _masks.Length * 2));
        }

        _masks[id] = new ResolvedMask { Filter = filter, Resolved = true };

        return filter;
    }

    // Resolves layer names to a filter of this world, interning each name as it goes. A name the
    // world has no room for throws here. Queries, Collider2D.Detects and KinematicBody2D's masks all
    // resolve names through this.
    internal CollisionFilter Intern(ReadOnlySpan<string> names)
    {
        CollisionFilter filter = CollisionFilter.None;
        foreach (string name in names)
        {
            filter = filter.With(Layer(name));
        }

        return filter;
    }
}
