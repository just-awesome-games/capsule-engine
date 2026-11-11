using System.Runtime.InteropServices;

namespace Capsule.Scenes;

// What one scene document's shared reads made of their arrays, by an array's exact bytes and the reader of its
// elements. Members authoring the same array, read the same way, share one read, whichever entries hold them.
internal sealed class SharedReads
{
    // How many bytes at each end of an array its hash covers. Arrays that differ only between them collide, and
    // compare unequal.
    private const int Sampled = 64;

    private readonly Lock _gate = new();
    private readonly Dictionary<Authored, object> _reads = [];

    // What a read kept of array as element reads it, or null when none has.
    internal object? Kept(ReadOnlyMemory<byte> array, Delegate element)
    {
        lock (_gate)
        {
            return _reads.GetValueOrDefault(new Authored(array, element));
        }
    }

    // Keeps read for array unless a racing composition kept one first, and returns the one kept.
    internal object Keep(ReadOnlyMemory<byte> array, Delegate element, object read)
    {
        lock (_gate)
        {
            ref object? kept = ref CollectionsMarshal.GetValueRefOrAddDefault(_reads, new Authored(array, element), out _);

            return kept ??= read;
        }
    }

    private readonly struct Authored(ReadOnlyMemory<byte> array, Delegate element) : IEquatable<Authored>
    {
        private readonly ReadOnlyMemory<byte> _array = array;
        private readonly Delegate _element = element;

        public bool Equals(Authored other) => _element.Equals(other._element) && _array.Span.SequenceEqual(other._array.Span);

        public override bool Equals(object? obj) => obj is Authored other && Equals(other);

        public override int GetHashCode()
        {
            ReadOnlySpan<byte> bytes = _array.Span;
            HashCode hash = default;
            hash.Add(bytes.Length);
            hash.AddBytes(bytes[..Math.Min(Sampled, bytes.Length)]);
            hash.AddBytes(bytes[^Math.Min(Sampled, bytes.Length)..]);

            return hash.ToHashCode();
        }
    }
}
