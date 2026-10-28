using System.Collections.Concurrent;
using System.Numerics;

namespace Capsule.Scenes;

// A run's state folded to 64 bits: the tick, the scene, the run's random source and each entity's type,
// world position and active state in tree order. A recording stores it and a replay compares it. The
// hash is this file's own, because HashCode and string hashing are seeded afresh in every process.
internal static class SimulationChecksum
{
    private const ulong Offset = 0xCBF29CE484222325UL;
    private const ulong Prime = 0x100000001B3UL;
    private const ulong Spread = 0x9E3779B97F4A7C15UL;
    private const ulong Fold = 0xBF58476D1CE4E5B9UL;

    // A type's identity, hashed once from its full name. A run sees few types, and a lookup allocates nothing.
    private static readonly ConcurrentDictionary<Type, ulong> TypeIdentities = new();

    // entities is false once the run has exited and its scene is torn down.
    internal static ulong Compute(long tick, Scene scene, RandomSource random, bool entities)
    {
        ulong hash = Offset;
        hash = Mix(hash, (ulong)tick);
        hash = Mix(hash, TypeIdentity(scene.GetType()));

        (ulong s0, ulong s1, ulong s2, ulong s3) = random.State;
        hash = Mix(hash, s0);
        hash = Mix(hash, s1);
        hash = Mix(hash, s2);
        hash = Mix(hash, s3);
        hash = Mix(hash, random.DrawCount);

        if (entities)
        {
            foreach (Entity entity in scene.Entities)
            {
                Vector2 position = entity.WorldPosition;
                hash = Mix(hash, TypeIdentity(entity.GetType()));
                hash = Mix(hash, ((ulong)BitConverter.SingleToUInt32Bits(position.X) << 32) | BitConverter.SingleToUInt32Bits(position.Y));
                hash = Mix(hash, (entity.Visible ? 1UL : 0UL) | (entity.Held ? 2UL : 0UL) | (entity.IsRemovalPending ? 4UL : 0UL));
            }
        }

        // The last mix leaves the low bits as well spread as the high ones.
        hash ^= hash >> 31;
        hash *= Fold;

        return hash ^ (hash >> 29);
    }

    // FNV-1a over the name's UTF-16 code units.
    private static ulong TypeIdentity(Type type) =>
        TypeIdentities.GetOrAdd(type, static key =>
        {
            ulong hash = Offset;
            foreach (char unit in key.FullName ?? key.Name)
            {
                hash = (hash ^ unit) * Prime;
            }

            return hash;
        });

    private static ulong Mix(ulong hash, ulong value) => BitOperations.RotateLeft(hash ^ (value * Spread), 29) * Prime;
}
