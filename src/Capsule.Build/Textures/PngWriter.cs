using System.Buffers.Binary;
using System.IO.Compression;

namespace Capsule.Build.Textures;

/// <summary>Encodes 8-bit greyscale or RGBA texels as a PNG, each row under the filter that leaves it smallest.</summary>
/// <remarks>Safe to call concurrently. The output depends on the texels alone.</remarks>
internal static class PngWriter
{
    private const int Filters = 5;

    private static readonly uint[] CrcTable = CreateCrcTable();

    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    /// <param name="channels">1 for greyscale, 4 for straight-alpha RGBA.</param>
    internal static void Write(ReadOnlySpan<byte> texels, int width, int height, int channels, Stream destination)
    {
        int stride = width * channels;
        using MemoryStream compressed = new();
        using (ZLibStream deflate = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            // One filtered candidate per filter, each prefixed with its filter byte.
            byte[] candidates = new byte[Filters * (stride + 1)];
            byte[] none = new byte[stride];
            for (int y = 0; y < height; y++)
            {
                ReadOnlySpan<byte> row = texels.Slice(y * stride, stride);
                ReadOnlySpan<byte> previous = y > 0 ? texels.Slice((y - 1) * stride, stride) : none;
                int best = 0;
                long smallest = long.MaxValue;
                for (int filter = 0; filter < Filters; filter++)
                {
                    Span<byte> candidate = candidates.AsSpan(filter * (stride + 1), stride + 1);
                    candidate[0] = (byte)filter;
                    long cost = Filter(filter, row, previous, channels, candidate[1..]);
                    if (cost < smallest)
                    {
                        smallest = cost;
                        best = filter;
                    }
                }

                deflate.Write(candidates, best * (stride + 1), stride + 1);
            }
        }

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = channels == 1 ? (byte)0 : (byte)6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;

        destination.Write(Signature);
        Chunk(destination, "IHDR"u8, header);
        Chunk(destination, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        Chunk(destination, "IEND"u8, []);
    }

    // Filters one row into filtered and returns the sum of its bytes read as signed, the usual
    // estimate of how well the row compresses. Each filter is its own loop, since this runs per byte.
    private static long Filter(int filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, int channels, Span<byte> filtered)
    {
        switch (filter)
        {
            case 0:
                row.CopyTo(filtered);
                break;
            case 1:
                row[..channels].CopyTo(filtered);
                for (int i = channels; i < row.Length; i++)
                {
                    filtered[i] = (byte)(row[i] - row[i - channels]);
                }

                break;
            case 2:
                for (int i = 0; i < row.Length; i++)
                {
                    filtered[i] = (byte)(row[i] - previous[i]);
                }

                break;
            case 3:
                for (int i = 0; i < row.Length; i++)
                {
                    int left = i >= channels ? row[i - channels] : 0;
                    filtered[i] = (byte)(row[i] - ((left + previous[i]) >> 1));
                }

                break;
            default:
                for (int i = 0; i < row.Length; i++)
                {
                    int left = i >= channels ? row[i - channels] : 0;
                    int corner = i >= channels ? previous[i - channels] : 0;
                    filtered[i] = (byte)(row[i] - SingleChannelPng.Paeth(left, previous[i], corner));
                }

                break;
        }

        long cost = 0;
        foreach (byte value in filtered)
        {
            cost += Math.Abs((int)(sbyte)value);
        }

        return cost;
    }

    private static void Chunk(Stream destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> field = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(field, data.Length);
        destination.Write(field);
        destination.Write(type);
        destination.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(field, ~Crc(Crc(uint.MaxValue, type), data));
        destination.Write(field);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
