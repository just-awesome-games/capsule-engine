using System.Buffers.Binary;
using System.IO.Compression;

namespace Capsule.Build.Textures;

/// <summary>Reads the one channel an <c>r8</c> texture carries: an 8-bit greyscale PNG's values, or an indexed PNG's raw palette indices.</summary>
internal static class SingleChannelPng
{
    // The palette is only an editor's preview and is never applied. Two entries of one colour stay two values.

    private const string Fix = "An r8 texture has one channel. Export it as 8-bit greyscale or indexed colour, not interlaced.";

    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>The values of <paramref name="png"/>, one byte a texel.</summary>
    /// <exception cref="FormatException">The file is no PNG, or a PNG of a kind with no single channel.</exception>
    internal static Texels Read(byte[] png)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new FormatException("is not a PNG.");
        }

        int width = 0;
        int height = 0;
        int depth = 0;
        using MemoryStream compressed = new();
        int at = Signature.Length;

        while (at + 8 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (length < 0 || at + 12 + (long)length > png.Length)
            {
                throw new FormatException("is a truncated PNG.");
            }

            ReadOnlySpan<byte> type = png.AsSpan(at + 4, 4);
            ReadOnlySpan<byte> data = png.AsSpan(at + 8, length);

            if (type.SequenceEqual("IHDR"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                depth = data[8];
                byte colour = data[9];
                bool greyscale = colour == 0 && depth == 8;
                bool indexed = colour == 3 && depth is 1 or 2 or 4 or 8;
                if (!greyscale && !indexed)
                {
                    throw new FormatException($"is a PNG of colour type {colour} at bit depth {depth}. {Fix}");
                }

                if (data[12] != 0)
                {
                    throw new FormatException($"is an interlaced PNG. {Fix}");
                }

                if (width <= 0 || height <= 0)
                {
                    throw new FormatException("is a PNG with no texels.");
                }
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            at += 12 + length;
        }

        if (depth == 0)
        {
            throw new FormatException("is a PNG with no header.");
        }

        int stride = ((width * depth) + 7) / 8;
        byte[] filtered = new byte[(stride + 1) * height];
        compressed.Position = 0;
        using (ZLibStream inflate = new(compressed, CompressionMode.Decompress))
        {
            try
            {
                inflate.ReadExactly(filtered);
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
            {
                throw new FormatException($"is a PNG whose image data is damaged: {ex.Message}", ex);
            }
        }

        byte[] values = new byte[width * height];
        byte[] previous = new byte[stride];
        byte[] row = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int start = y * (stride + 1);
            filtered.AsSpan(start + 1, stride).CopyTo(row);
            Unfilter(filtered[start], row, previous);
            Unpack(row, depth, values.AsSpan(y * width, width));
            (previous, row) = (row, previous);
        }

        return new Texels(values, width, height, 1);
    }

    // Reverses one row's filter in place. Every accepted kind is one byte a texel or less, so the
    // byte to the left is always one byte back.
    private static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> previous)
    {
        for (int i = 0; i < row.Length; i++)
        {
            int left = i > 0 ? row[i - 1] : 0;
            int up = previous[i];
            int corner = i > 0 ? previous[i - 1] : 0;
            row[i] = (byte)(row[i] + filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, corner),
                _ => throw new FormatException($"is a PNG with the unknown row filter {filter}."),
            });
        }
    }

    private static int Paeth(int left, int up, int corner)
    {
        int estimate = left + up - corner;
        int toLeft = Math.Abs(estimate - left);
        int toUp = Math.Abs(estimate - up);
        int toCorner = Math.Abs(estimate - corner);

        return toLeft <= toUp && toLeft <= toCorner ? left : toUp <= toCorner ? up : corner;
    }

    // Sub-byte texels are packed from the most significant bit.
    private static void Unpack(ReadOnlySpan<byte> row, int depth, Span<byte> values)
    {
        if (depth == 8)
        {
            row[..values.Length].CopyTo(values);
            return;
        }

        int perByte = 8 / depth;
        int mask = (1 << depth) - 1;
        for (int x = 0; x < values.Length; x++)
        {
            int shift = 8 - (depth * ((x % perByte) + 1));
            values[x] = (byte)((row[x / perByte] >> shift) & mask);
        }
    }
}
