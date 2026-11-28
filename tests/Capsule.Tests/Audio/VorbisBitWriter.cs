namespace Capsule.Tests.Audio;

// Hand-built Vorbis headers and packets. Vorbis packs fields least significant bit first.
internal sealed class VorbisBitWriter
{
    private readonly List<byte> _bytes = [];
    private int _bit;

    internal void Write(int value, int bits)
    {
        for (int i = 0; i < bits; i++)
        {
            if (_bit == 0)
            {
                _bytes.Add(0);
            }

            if (((value >> i) & 1) != 0)
            {
                _bytes[^1] |= (byte)(1 << _bit);
            }

            _bit = (_bit + 1) % 8;
        }
    }

    internal void Write(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            Write(value, 8);
        }
    }

    // The bytes written, padded with zeros so a reader running past the last field reads zeros.
    internal byte[] ToArray() => [.. _bytes, 0, 0, 0, 0, 0, 0, 0, 0];
}
