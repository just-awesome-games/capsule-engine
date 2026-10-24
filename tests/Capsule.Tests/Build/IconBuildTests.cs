using System.Buffers.Binary;
using StbImageSharp;

namespace Capsule.Tests.Build;

/// <summary>
/// The asset root's <c>Icon.png</c> becomes the shell's icon pair, an all-PNG <c>.ico</c> and a 128x128
/// window bitmap, and is never an asset.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class IconBuildTests
{
    private const int Side = 256;

    // 32 texels, a whole number of texels at every side the .ico holds.
    private const int Block = 32;

    private const string IconDirectory = ToolWorkspace.Out + "/icon/";

    // An 8x8 grid of blocks, half of them translucent. Block (4, 0) is transparent but for a red stripe
    // over source columns 129 to 136, which no side's texels align with.
    [Fact]
    public void AnIconSource_DerivesEveryEntryAsAreaAveragesThatKeepColourBesideTransparency()
    {
        using ToolWorkspace workspace = new();
        byte[] texels = new byte[Side * Side * 4];
        for (int y = 0; y < Side; y++)
        {
            for (int x = 0; x < Side; x++)
            {
                byte[] texel = (x / Block, y / Block) == (4, 0)
                    ? x is >= 129 and < 137 ? [255, 0, 0, 255] : [0, 0, 0, 0]
                    : BlockColour(x / Block, y / Block);
                texel.CopyTo(texels, ((y * Side) + x) * 4);
            }
        }

        workspace.WritePng("Assets/Icon.png", texels, Side, Side);
        workspace.Write("Assets/Textures/hero.png", string.Empty);
        workspace.Succeed();

        byte[] ico = File.ReadAllBytes(IconDirectory + "Icon.ico");
        int[] sides = [16, 24, 32, 48, 64, 128, 256];
        Assert.Equal(sides.Length, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));
        ImageResult[] entries = new ImageResult[sides.Length];
        for (int i = 0; i < sides.Length; i++)
        {
            ReadOnlySpan<byte> entry = ico.AsSpan(6 + (16 * i), 16);
            entries[i] = ImageResult.FromMemory(
                ico.AsSpan(BinaryPrimitives.ReadInt32LittleEndian(entry[12..]), BinaryPrimitives.ReadInt32LittleEndian(entry[8..])).ToArray(),
                ColorComponents.RedGreenBlueAlpha);
            Assert.Equal((sides[i] % 256, sides[i] % 256, sides[i], sides[i]), (entry[0], entry[1], entries[i].Width, entries[i].Height));
            for (int y = 0; y < sides[i]; y++)
            {
                for (int x = 0; x < sides[i]; x++)
                {
                    (int column, int row) = (x * Side / Block / sides[i], y * Side / Block / sides[i]);
                    if ((column, row) != (4, 0))
                    {
                        Assert.Equal(BlockColour(column, row), Texel(entries[i], x, y));
                    }
                }
            }
        }

        // At 16, the stripe is half of the one texel it lies in.
        Assert.Equal([255, 0, 0, 128], Texel(entries[0], 8, 0));
        ImageResult window = ImageResult.FromMemory(File.ReadAllBytes(IconDirectory + "Icon.bmp"), ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((128, 128), (window.Width, window.Height));
        Assert.Equal(entries[5].Data, window.Data);
        Assert.Equal(
            [[255, 0, 0, 128], [255, 0, 0, 255], [255, 0, 0, 255], [255, 0, 0, 255], [255, 0, 0, 128], [0, 0, 0, 0]],
            Enumerable.Range(64, 6).Select(x => Texel(window, x, 0)));

        Assert.Equal(["textures/hero.png"], workspace.Shipped);
        Assert.DoesNotContain("Icon", workspace.Generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(256, 255, "and an application icon is square")]
    [InlineData(128, 128, "under the 256x256 an application icon needs")]
    public void AnIconSourceNotSquareOrUnder256_FailsNamingTheFix(int width, int height, string defect)
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Icon.png", width, height);

        Assert.Equal(
            $"Assets/Icon.png: is {width}x{height} texels, {defect}. Export it square at 256x256 or larger. Pixel art scales up by a whole factor.{Environment.NewLine}",
            workspace.Fail());
        Assert.False(File.Exists(IconDirectory + "Icon.ico"));
    }

    private static byte[] BlockColour(int column, int row) =>
        [(byte)(column * 32), (byte)(row * 32), (byte)(255 - (column * 16)), (column + row) % 2 == 0 ? (byte)255 : (byte)160];

    private static byte[] Texel(ImageResult image, int x, int y) => image.Data.AsSpan(((y * image.Width) + x) * 4, 4).ToArray();
}
