using System.Numerics;

namespace Capsule.Physics.Internal;

// The corners of every polygon past Shape2D's inline count, once per distinct outline, for the life of the
// process. A shape holds an index here rather than the array, because a struct holding a reference costs
// every query that copies a shape. Index 0 is no hull. Adding takes a lock. Reading takes none, since an
// entry never changes and the table is only ever replaced by a longer copy of itself.
internal static class HullStore2D
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<Vector2[], int> Indices = new(OutlineComparer.Instance);
    private static Vector2[][] Hulls = new Vector2[16][];
    private static int Next = 1;

    internal static Vector2[] Get(int index) => Volatile.Read(ref Hulls)[index];

    // The index of these corners, storing them the first time they are seen.
    internal static int Add(ReadOnlySpan<Vector2> corners)
    {
        Vector2[] hull = corners.ToArray();

        lock (Gate)
        {
            if (Indices.TryGetValue(hull, out int index))
            {
                return index;
            }

            if (Next == Hulls.Length)
            {
                Vector2[][] longer = new Vector2[Hulls.Length * 2][];
                Array.Copy(Hulls, longer, Next);
                Volatile.Write(ref Hulls, longer);
            }

            Hulls[Next] = hull;
            Indices.Add(hull, Next);

            return Next++;
        }
    }

    private sealed class OutlineComparer : IEqualityComparer<Vector2[]>
    {
        internal static readonly OutlineComparer Instance = new();

        public bool Equals(Vector2[]? x, Vector2[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(Vector2[] obj)
        {
            HashCode hash = default;
            foreach (Vector2 corner in obj)
            {
                hash.Add(corner);
            }

            return hash.ToHashCode();
        }
    }
}
