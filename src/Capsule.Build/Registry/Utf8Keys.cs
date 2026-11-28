using System.Text;

namespace Capsule.Build.Registry;

/// <summary>Keys as the engine's <c>PackedKeys</c> searches them: UTF-8 end to end, in ordinal order of their bytes.</summary>
internal static class Utf8Keys
{
    private static readonly Comparer<byte[]> ByteOrder = Comparer<byte[]>.Create(static (x, y) => x.AsSpan().SequenceCompareTo(y));

    /// <summary>Each text's exclusive end in the UTF-8 of every text end to end.</summary>
    internal static IEnumerable<int> Ends(IEnumerable<string> texts)
    {
        int end = 0;
        foreach (string text in texts)
        {
            yield return end += Encoding.UTF8.GetByteCount(text);
        }
    }

    /// <summary>Each integer as the four bytes of a little-endian 32-bit integer, which a span of constant bytes holds.</summary>
    internal static IEnumerable<byte> Packed(IEnumerable<int> integers) =>
        integers.SelectMany(static value => new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) });

    /// <summary>The items in ordinal order of their keys' UTF-8 bytes, each key encoded once.</summary>
    /// <remarks>The order differs from ordinal order of UTF-16 units only past a surrogate.</remarks>
    internal static IEnumerable<T> Sorted<T>(IEnumerable<T> items, Func<T, string> key) =>
        items.OrderBy(item => Encoding.UTF8.GetBytes(key(item)), ByteOrder);
}
