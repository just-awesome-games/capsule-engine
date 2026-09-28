using System.Buffers.Binary;
using System.IO.Compression;
using Capsule.Build.Textures;
using Capsule.Runtime.Assets;
using Capsule.Tests.Documents;
using StbImageSharp;

namespace Capsule.Tests.Build;

/// <summary>
/// The r8 reader against stb's own decode, through every row filter and every accepted bit depth, and
/// an r8 texture's values surviving the build and the runtime's decode unchanged.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class SingleChannelPngTests
{
    private const int Width = 13;

    // Every row filter, twice over.
    private const int Height = 10;

    // An indexed file's indices through its own palette are exactly what stb decodes it to, and a
    // greyscale file's values are stb's one-component decode.
    [Theory]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    [InlineData(3, 8)]
    [InlineData(0, 8)]
    public void TheReader_MatchesTheReferenceDecodeThroughEveryFilter(byte colour, byte depth)
    {
        byte[] values = Values(depth);
        byte[] palette = [.. Enumerable.Range(0, (1 << depth) * 3).Select(static i => (byte)((i * 97) + 13))];
        byte[] png = Png(values, colour, depth, colour == 3 ? palette : null);

        Texels read = SingleChannelPng.Read(png);

        Assert.Equal((Width, Height, 1), (read.Width, read.Height, read.Channels));
        if (colour == 0)
        {
            Assert.Equal(ImageResult.FromMemory(png, ColorComponents.Grey).Data, read.Data);
        }
        else
        {
            byte[] mapped = [.. read.Data.SelectMany(index => new[] { palette[index * 3], palette[(index * 3) + 1], palette[(index * 3) + 2], (byte)255 })];
            Assert.Equal(ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha).Data, mapped);
        }
    }

    [Fact]
    public void AnRgbSource_IsRefusedNamingTheFix()
    {
        byte[] png = Png(new byte[Width * Height * 3], colour: 2, depth: 8, palette: null, channels: 3);

        FormatException error = Assert.Throws<FormatException>(() => SingleChannelPng.Read(png));

        Assert.Contains("Export it as 8-bit greyscale or indexed colour", error.Message, StringComparison.Ordinal);
    }

    // The runtime reads the shipped file as one channel with no premultiply, so every value lands as
    // the author's index.
    [Fact]
    public void AnR8Texture_DecodesAtRunTimeToItsExactValues()
    {
        using ToolWorkspace workspace = new();
        byte[] values = Values(4);
        workspace.Write("Assets/mask.png", Png(values, colour: 3, depth: 4, palette: new byte[16 * 3]));
        workspace.Write("Assets/mask.png.config.json", """{ "format": "r8" }""");
        workspace.Succeed();

        using FileStream shipped = File.OpenRead(Path.Combine(ToolWorkspace.Out, "assets", "mask.png"));
        DecodedTexture decoded = TextureDecoder.Decode(shipped, new TexelPool(), "mask", singleChannel: true);

        Assert.Equal((Width, Height, 1), (decoded.Width, decoded.Height, decoded.BytesPerTexel));
        Assert.Equal(values, decoded.Texels);
    }

    // Values that fill the depth's range and vary along and across rows, so every filter sees
    // non-zero left, up and corner neighbours.
    private static byte[] Values(int depth) =>
        [.. Enumerable.Range(0, Width * Height).Select(i => (byte)(((i * 7) + (i / Width * 3)) % (1 << depth)))];

    // A PNG whose row y is written with filter y % 5. Values are one per texel, or channels per texel
    // for a colour type with more than one.
    private static byte[] Png(byte[] values, byte colour, byte depth, byte[]? palette, int channels = 1)
    {
        int stride = ((Width * depth * channels) + 7) / 8;
        using MemoryStream raw = new();
        byte[] previous = new byte[stride];
        for (int y = 0; y < Height; y++)
        {
            byte[] row = new byte[stride];
            for (int x = 0; x < Width * channels; x++)
            {
                int bit = x * depth;
                row[bit / 8] |= (byte)(values[(y * Width * channels) + x] << (8 - depth - (bit % 8)));
            }

            byte filter = (byte)(y % 5);
            raw.WriteByte(filter);
            for (int i = 0; i < stride; i++)
            {
                int left = i >= channels ? row[i - channels] : 0;
                int up = previous[i];
                int corner = i >= channels ? previous[i - channels] : 0;
                int predicted = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, corner),
                    _ => 0,
                };
                raw.WriteByte((byte)(row[i] - predicted));
            }

            previous = row;
        }

        using MemoryStream compressed = new();
        using (ZLibStream deflate = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(deflate);
        }

        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = depth;
        header[9] = colour;

        using MemoryStream png = new();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Chunk(png, "IHDR", header);
        if (palette is not null)
        {
            Chunk(png, "PLTE", palette);
        }

        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);

        return png.ToArray();
    }

    private static int Paeth(int left, int up, int corner)
    {
        int estimate = left + up - corner;
        int toLeft = Math.Abs(estimate - left);
        int toUp = Math.Abs(estimate - up);
        int toCorner = Math.Abs(estimate - corner);

        return toLeft <= toUp && toLeft <= toCorner ? left : toUp <= toCorner ? up : corner;
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        byte[] typed = [.. type.Select(static character => (byte)character), .. data];
        byte[] crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typed));
        png.Write(length);
        png.Write(typed);
        png.Write(crc);
    }

    private static uint Crc(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
