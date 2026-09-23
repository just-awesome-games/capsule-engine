namespace Capsule;

// Hashed one-dimensional value noise in [-1, 1]. Each whole lattice cell holds a hashed value, and a
// sample between two cells blends them on a smoothstep. It is arithmetic only and gives the same bits
// on every platform. A channel is an independent stream over the same lattice.
internal static class ValueNoise
{
    // The phase is a whole cell plus a fraction in [0, 1), which keeps its precision over any run length.
    internal static float Sample(uint channel, long cell, float fraction)
    {
        float low = Lattice(channel, cell);
        float high = Lattice(channel, cell + 1);
        float blend = fraction * fraction * (3f - (2f * fraction));

        return low + ((high - low) * blend);
    }

    // A 64-bit finalizer mix of the cell and channel. The top 24 bits scale exactly into [-1, 1).
    // The cell multiplier is 2^64 over the golden ratio, which spreads consecutive cells across the range,
    // and the channel multiplier is any odd, well-mixed constant that keeps channels apart. The shift and
    // multiply steps are MurmurHash3's 64-bit finalizer, fmix64.
    private static float Lattice(uint channel, long cell)
    {
        unchecked
        {
            ulong hash = ((ulong)cell * 0x9E3779B97F4A7C15UL) ^ (channel * 0xD1B54A32D192ED03UL);
            hash ^= hash >> 33;
            hash *= 0xFF51AFD7ED558CCDUL;
            hash ^= hash >> 33;
            hash *= 0xC4CEB9FE1A85EC53UL;
            hash ^= hash >> 33;

            return ((hash >> 40) * (2f / (1 << 24))) - 1f;
        }
    }
}
