using System.Numerics;

namespace Capsule.Physics.Internal;

// The pair tests the world's walks run on each candidate. A box mover meets a box or an axis-aligned
// edge in closed form, and every other pair goes through GJK.
internal static class Narrowphase2D
{
    // How far a shape already reaches past the line through `point` along `normal`, measured inwards.
    // An edge is one-sided. A shape starting more than a slop beyond it has passed through and meets
    // nothing.
    internal static float ReachPastEdge(in Shape2D shape, Vector2 point, Vector2 normal) =>
        Vector2.Dot(point - shape.Support(-normal), normal) + shape.Radius;

    // How far a mover reaches past the far side of a one-way collider along its surface normal. A
    // mover more than a slop past has started inside it or beyond it, and passes through.
    internal static float ReachPastOneWay(in Shape2D moving, in Shape2D target, Vector2 normal) =>
        Vector2.Dot(target.Support(normal), normal) + target.Radius
        - (Vector2.Dot(moving.Support(-normal), normal) - moving.Radius);

    // The normal a sweep meets an edge along, where the box stands at `box` and the edge's nearest point
    // is `point`. A box face meeting the end of an edge rests on that endpoint, and its face's axis is
    // the surface there, not the edge's slope. A corner meeting the edge just short of its end has the
    // endpoint nearest too, but lies off the face, and stops on the edge's slope. The narrowphase normal
    // carries rounding error. It only chooses between the two exact candidates.
    internal static Vector2 EdgeNormal(in Shape2D moving, in Aabb2D box, Vector2 point, Vector2 swept, Vector2 outward)
    {
        if (moving.Kind != ShapeKind2D.Box)
        {
            return outward;
        }

        bool across = MathF.Abs(swept.X) >= MathF.Abs(swept.Y);
        Vector2 axis = across ? new Vector2(MathF.Sign(swept.X), 0f) : new Vector2(0f, MathF.Sign(swept.Y));
        bool onFace = across
            ? point.Y >= box.Min.Y && point.Y <= box.Max.Y
            : point.X >= box.Min.X && point.X <= box.Max.X;

        return onFace && Vector2.Dot(swept, axis) > Vector2.Dot(swept, outward) && Vector2.Dot(axis, outward) > 0f
            ? axis
            : outward;
    }

    // Whether a mover starts across an edge rather than on its outward side. Reaching past the edge's
    // line is not enough. A box beside the top corner of a step reaches past the line of the step's
    // slope while still standing clear of the slope itself, and that corner must stop it.
    internal static bool StartedPast(in Shape2D moving, Vector2 start, Vector2 end, Vector2 outward) =>
        ReachPastEdge(moving, start, outward) > CollisionTolerance.LinearSlop
        && Separation(moving, Shape2D.Segment(start, end), out _, out _) <= 0f;

    // Whether a one-way collider stops a mover meeting it with this normal. It blocks only on a surface
    // it keeps, and only a mover that started clear of it.
    internal static bool OneWayBlocks(in Shape2D moving, in Shape2D target, Vector2 normal, bool solidSides) =>
        CollisionGrid2D.OneWayKeeps(normal, solidSides) && ReachPastOneWay(moving, target, normal) <= CollisionTolerance.LinearSlop;

    // How far apart two shapes are, negative when they overlap, with the surface point and the normal
    // on the second. Overlapping hulls report the least overlap and the shortest way out.
    internal static float Separation(in Shape2D shape, in Shape2D other, out Vector2 normal, out Vector2 point)
    {
        if (shape.Kind == ShapeKind2D.Box && IsBoxLike(other))
        {
            return Boxes2D.Separation(shape.Bounds, other.Bounds, out normal, out point);
        }

        float separation = Gjk2D.Separation(shape, other, out normal, out point);
        if (normal != Vector2.Zero)
        {
            return separation;
        }

        float depth = OverlapDepth(shape, other, out normal);
        point = other.Support(normal) + (normal * other.Radius);

        return -depth;
    }

    // The same against a grid cell or one of its faces, which stays an Aabb2D for a box mover. Only the
    // iterated routines read the hull a Shape2D carries.
    internal static float Separation(in Shape2D shape, in Aabb2D cell, out Vector2 normal, out Vector2 point) =>
        shape.Kind == ShapeKind2D.Box
            ? Boxes2D.Separation(shape.Bounds, cell, out normal, out point)
            : Separation(shape, Shape2D.OfCell(cell), out normal, out point);

    // How far a pair overlaps, from its signed separation. A pair within the skin but apart overlaps by nothing.
    internal static float DepthOf(float separation) => MathF.Max(0f, -separation);

    // Whether a shape starts more than a skin inside another, with the shortest way out pointing from
    // the target towards the shape.
    internal static bool StartsInside(in Shape2D moving, in Shape2D target, out Vector2 normal)
    {
        normal = Vector2.Zero;
        if (!moving.Bounds.Overlaps(target.Bounds))
        {
            return false;
        }

        if (moving.Kind == ShapeKind2D.Box && IsBoxLike(target))
        {
            return -Boxes2D.Separation(moving.Bounds, target.Bounds, out normal, out _) > CollisionTolerance.ContactSkin;
        }

        float separation = Gjk2D.Separation(moving, target, out normal, out _);

        return (normal != Vector2.Zero ? -separation : OverlapDepth(moving, target, out normal)) > CollisionTolerance.ContactSkin;
    }

    // The same against a grid cell, which stays an Aabb2D for a box mover. Most cells a sweep passes lie
    // clear of where it starts, and their bounds turn them away before any shape is built.
    internal static bool StartsInside(in Shape2D moving, in Aabb2D cell, out Vector2 normal)
    {
        normal = Vector2.Zero;
        if (!moving.Bounds.Overlaps(cell))
        {
            return false;
        }

        return moving.Kind == ShapeKind2D.Box
            ? -Boxes2D.Separation(moving.Bounds, cell, out normal, out _) > CollisionTolerance.ContactSkin
            : StartsInside(moving, Shape2D.OfCell(cell), out normal);
    }

    // The fraction of translation at which moving first touches target, with the contact point and
    // normal there. Conservative advancement has no time of impact to report out of an existing touch.
    // A pair already within the skin is decided by whether the sweep drives into it.
    internal static bool Sweep(
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

    // The same against a grid cell or one of its faces, which a box mover meets without a Shape2D.
    internal static bool Sweep(
        in Shape2D moving,
        Vector2 translation,
        in Aabb2D cell,
        out float fraction,
        out Vector2 normal,
        out Vector2 point) =>
        moving.Kind == ShapeKind2D.Box
            ? SweepBoxes(moving.Bounds, translation, cell, out fraction, out normal, out point)
            : Sweep(moving, translation, Shape2D.OfCell(cell), out fraction, out normal, out point);

    // A box and an axis-aligned segment take the closed form against a box mover. A slanted segment
    // has no box to stand for it.
    private static bool IsBoxLike(in Shape2D shape) =>
        shape.Kind == ShapeKind2D.Box
        || (shape.Kind == ShapeKind2D.Segment
            && (shape.Bounds.Min.X == shape.Bounds.Max.X || shape.Bounds.Min.Y == shape.Bounds.Max.Y));

    // How far two shapes whose hulls overlap reach into each other, with the shortest way out for
    // moving. GJK measures no depth there, and the least overlap across the faces of both hulls does.
    // Two circles' cores have no face. Their radii are then the depth, along the boxes'
    // least-penetration axis.
    private static float OverlapDepth(in Shape2D moving, in Shape2D target, out Vector2 normal)
    {
        float depth = float.PositiveInfinity;
        normal = Vector2.Zero;
        LeastOverlap(moving, moving, target, ref depth, ref normal);
        LeastOverlap(target, moving, target, ref depth, ref normal);
        if (float.IsPositiveInfinity(depth))
        {
            depth = moving.Radius + target.Radius;
            Boxes2D.Separation(moving.Bounds, target.Bounds, out normal, out _);
        }

        return depth;
    }

    // Keeps the least overlap of two shapes along the face normals of `hull`, radii included, with the
    // way out along it. A capsule's core is a segment with one axis, and a circle's has none.
    private static void LeastOverlap(in Shape2D hull, in Shape2D moving, in Shape2D target, ref float depth, ref Vector2 normal)
    {
        int axes = hull.PointCount < 3 ? hull.PointCount - 1 : hull.PointCount;
        for (int index = 0; index < axes; index++)
        {
            Vector2 axis = hull.EdgeNormal(index);
            float forward = Vector2.Dot(target.Support(axis), axis) + target.Radius
                - (Vector2.Dot(moving.Support(-axis), axis) - moving.Radius);
            float backward = Vector2.Dot(moving.Support(axis), axis) + moving.Radius
                - (Vector2.Dot(target.Support(-axis), axis) - target.Radius);
            float overlap = MathF.Min(forward, backward);
            if (overlap < depth)
            {
                depth = overlap;
                normal = forward <= backward ? axis : -axis;
            }
        }
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
}
