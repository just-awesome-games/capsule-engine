using System.Numerics;

namespace Capsule.Collision.Internal;

/// <summary>The closed-form axis-aligned narrowphase the box cases take.</summary>
internal static class Boxes
{
    /// <summary>
    /// How far apart two boxes are, negative when they overlap, with a closest surface point on
    /// <paramref name="b"/>. <paramref name="normal"/> is the unit direction from
    /// <paramref name="b"/> towards <paramref name="a"/> — for an overlap, the axis of least
    /// penetration.
    /// </summary>
    internal static float Separation(in Aabb2D a, in Aabb2D b, out Vector2 normal, out Vector2 point)
    {
        float lowX = b.Min.X - a.Max.X;
        float highX = a.Min.X - b.Max.X;
        float lowY = b.Min.Y - a.Max.Y;
        float highY = a.Min.Y - b.Max.Y;

        float gapX = MathF.Max(lowX, highX);
        float gapY = MathF.Max(lowY, highY);
        Vector2 axisX = new(lowX >= highX ? -1f : 1f, 0f);
        Vector2 axisY = new(0f, lowY >= highY ? -1f : 1f);

        if (gapX > 0f && gapY > 0f)
        {
            // Apart on both axes: the corners are closest, so neither axis alone is the normal.
            normal = Vector2.Normalize(new Vector2(axisX.X * gapX, axisY.Y * gapY));
            point = Vector2.Clamp(a.Center, b.Min, b.Max);
            return MathF.Sqrt((gapX * gapX) + (gapY * gapY));
        }

        if (gapX >= gapY)
        {
            normal = axisX;
            point = new Vector2(normal.X < 0f ? b.Min.X : b.Max.X, Math.Clamp(a.Center.Y, b.Min.Y, b.Max.Y));
            return gapX;
        }

        normal = axisY;
        point = new Vector2(Math.Clamp(a.Center.X, b.Min.X, b.Max.X), normal.Y < 0f ? b.Min.Y : b.Max.Y);
        return gapY;
    }

    /// <summary>
    /// The fraction of <paramref name="translation"/> at which <paramref name="moving"/> first
    /// touches <paramref name="target"/> over an extent, exactly. Returns false when they never
    /// touch and when they only ever meet along a line of zero width;
    /// <paramref name="fraction"/> is 0 when they already overlap.
    /// </summary>
    internal static bool Sweep(
        in Aabb2D moving,
        Vector2 translation,
        in Aabb2D target,
        out float fraction,
        out Vector2 normal)
    {
        fraction = 0f;
        normal = Vector2.Zero;

        // Flush on an axis the translation does not move along, the pair meets over no extent for
        // the whole sweep, which is not a crossing. The ray below cannot say so: its entry there is
        // degenerate and the normal falls through to the least-penetration axis, whose exact tie
        // resolves towards X and would answer differently for a Left face than for a Top.
        if ((translation.X == 0f && (moving.Max.X == target.Min.X || moving.Min.X == target.Max.X))
            || (translation.Y == 0f && (moving.Max.Y == target.Min.Y || moving.Min.Y == target.Max.Y)))
        {
            return false;
        }

        // Minkowski form: the moving box shrinks to its centre and the target grows by its half
        // extents, turning the sweep into one ray against one box.
        Vector2 half = moving.Size * 0.5f;
        Aabb2D grown = new(target.Min - half, target.Max + half);

        if (!Segments.RayBox(grown, moving.Center, translation, 1f, out fraction, out normal))
        {
            return false;
        }

        if (fraction == 0f && normal == Vector2.Zero)
        {
            // Already overlapping, so the sweep has no entry face; the least-penetration axis is
            // the only normal the pair defines.
            Separation(moving, target, out normal, out _);
        }

        return true;
    }
}
