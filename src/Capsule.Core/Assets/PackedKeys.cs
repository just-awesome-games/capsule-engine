using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace Capsule.Assets;

/// <summary>Searches the keys the build packed end to end as UTF-8. Generated code calls it.</summary>
/// <remarks>Every offset and index is a little-endian 32-bit integer, since a span of constant bytes reads with no allocation in any build configuration.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PackedKeys
{
    /// <summary>The index of <paramref name="key"/> among <paramref name="keys"/>, or -1 when it is not there.</summary>
    /// <remarks>Only a scene's composition searches. A key too long for the stack allocates its bytes.</remarks>
    /// <param name="keys">Every key's UTF-8 bytes, end to end.</param>
    /// <param name="ends">Each key's exclusive end in <paramref name="keys"/>.</param>
    /// <param name="order">Each key's index in ordinal order of the keys' bytes, or empty when the keys are packed in that order.</param>
    /// <param name="key">The key to find.</param>
    public static int Find(ReadOnlySpan<byte> keys, ReadOnlySpan<byte> ends, ReadOnlySpan<byte> order, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        const int StackBytes = 256;
        int length = Encoding.UTF8.GetByteCount(key);
        Span<byte> query = length <= StackBytes ? stackalloc byte[StackBytes] : new byte[length];
        query = query[..Encoding.UTF8.GetBytes(key, query)];

        int low = 0;
        int high = (ends.Length / sizeof(int)) - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int index = order.IsEmpty ? middle : At(order, middle);
            int comparison = Entry(keys, ends, index).SequenceCompareTo(query);
            if (comparison == 0)
            {
                return index;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ReadOnlySpan<byte> Entry(ReadOnlySpan<byte> packed, ReadOnlySpan<byte> ends, int index) =>
        packed[(index == 0 ? 0 : At(ends, index - 1))..At(ends, index)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int At(ReadOnlySpan<byte> integers, int index) => BinaryPrimitives.ReadInt32LittleEndian(integers[(index * sizeof(int))..]);
}
