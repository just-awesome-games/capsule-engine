using System.Numerics;

namespace Capsule;

// Hashed value noise in [-1, 1]. Each whole lattice cell holds a hashed value, and a sample between
// cells blends them on a smoothstep. It is arithmetic only and gives the same bits on every platform.
// A channel is an independent stream over the same lattice. The one-dimensional lattice is the
// two-dimensional one's row 0.
internal static class ValueNoise
{
    // The phase is a whole cell plus a fraction in [0, 1), which keeps its precision over any run length.
    internal static float Sample(uint channel, long cell, float fraction)
    {
        float low = Lattice(channel, cell, 0);
        float high = Lattice(channel, cell + 1, 0);

        return low + ((high - low) * Smooth(fraction));
    }

    // The two-dimensional field's slope at a point in lattice cells, divided by three so each axis lies
    // in [-1, 1]. It is exact, taken from the same four lattice values the field blends.
    internal static Vector2 Slope(uint channel, Vector2 point)
    {
        float floorX = MathF.Floor(point.X);
        float floorY = MathF.Floor(point.Y);
        long x = (long)floorX;
        long y = (long)floorY;
        float fx = point.X - floorX;
        float fy = point.Y - floorY;

        float v00 = Lattice(channel, x, y);
        float v10 = Lattice(channel, x + 1, y);
        float v01 = Lattice(channel, x, y + 1);
        float v11 = Lattice(channel, x + 1, y + 1);
        float twist = v11 - v01 - v10 + v00;

        // The smoothstep's slope, 6f(1 - f), peaks at 1.5. Two cell values at most 2 apart and that third
        // put each axis in [-1, 1].
        return new Vector2(
            2f * fx * (1f - fx) * (v10 - v00 + (twist * Smooth(fy))),
            2f * fy * (1f - fy) * (v01 - v00 + (twist * Smooth(fx))));
    }

    private static float Smooth(float fraction) => fraction * fraction * (3f - (2f * fraction));

    // A 64-bit finalizer mix of the cell and channel. The top 24 bits scale exactly into [-1, 1).
    // The X multiplier is 2^64 over the golden ratio, which spreads consecutive cells across the range,
    // and the Y and channel multipliers are odd, well-mixed constants that keep rows and channels apart.
    // The shift and multiply steps are MurmurHash3's 64-bit finalizer, fmix64.
    private static float Lattice(uint channel, long x, long y)
    {
        unchecked
        {
            ulong hash = ((ulong)x * 0x9E3779B97F4A7C15UL) ^ ((ulong)y * 0xC2B2AE3D27D4EB4FUL) ^ (channel * 0xD1B54A32D192ED03UL);
            hash ^= hash >> 33;
            hash *= 0xFF51AFD7ED558CCDUL;
            hash ^= hash >> 33;
            hash *= 0xC4CEB9FE1A85EC53UL;
            hash ^= hash >> 33;

            return ((hash >> 40) * (2f / (1 << 24))) - 1f;
        }
    }
}
