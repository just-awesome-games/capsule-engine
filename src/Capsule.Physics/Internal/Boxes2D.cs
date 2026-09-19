using System.Numerics;

namespace Capsule.Physics.Internal;

// Closed-form narrowphase for axis-aligned box pairs.
internal static class Boxes2D
{
    // How far apart two boxes are, negative when they overlap, with a closest surface point on b.
    // normal is the unit direction from b towards a, and for an overlap the axis of least penetration.
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
            // Apart on both axes, so the closest features are corners and the normal is diagonal.
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

    // The fraction of translation at which moving first touches target over a non-zero extent.
    // Returns false when they never touch, or meet only along a line of zero width. Fraction is 0
    // when they already overlap.
    internal static bool Sweep(
        in Aabb2D moving,
        Vector2 translation,
        in Aabb2D target,
        out float fraction,
        out Vector2 normal)
    {
        fraction = 0f;
        normal = Vector2.Zero;

        // A pair flush on an axis the translation does not move along meets over no extent for the
        // whole sweep, which is not a crossing. The ray below cannot tell. Its entry there is
        // degenerate and the normal falls through to the least-penetration axis, whose tie resolves
        // towards X and would answer differently for a Left face than for a Top.
        if ((translation.X == 0f && (moving.Max.X == target.Min.X || moving.Min.X == target.Max.X))
            || (translation.Y == 0f && (moving.Max.Y == target.Min.Y || moving.Min.Y == target.Max.Y)))
        {
            return false;
        }

        // Minkowski form. The moving box shrinks to its centre and the target grows by its half
        // extents, turning the sweep into a ray against one box.
        Vector2 half = moving.Size * 0.5f;
        Aabb2D grown = new(target.Min - half, target.Max + half);

        if (!Rays2D.RayBox(grown, moving.Center, translation, 1f, out fraction, out normal))
        {
            return false;
        }

        if (fraction == 0f && normal == Vector2.Zero)
        {
            // Already overlapping, so there is no entry face and the least-penetration axis supplies
            // the normal.
            Separation(moving, target, out normal, out _);
        }

        return true;
    }
}
