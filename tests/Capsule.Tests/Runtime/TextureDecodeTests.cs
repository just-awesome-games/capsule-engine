using System.Security.Cryptography;
using Capsule.Assets;
using Capsule.Runtime.Assets;
using Capsule.Tests.Build;
using StbImageSharp;

namespace Capsule.Tests.Runtime;

public sealed class TextureDecodeTests
{
    // The hashes are the premultiplied texels captured from each file. A changed hash is a visible change to every shipped texture.
    [Theory]
    [InlineData("alpha-ramp.png", 256, 6, "61DE40409A9BBE13A6E27012AED7EBF96F7B0F23E6EED86A89D8AD9C3B566207")]
    [InlineData("hd-atlas.png", 2048, 2048, "228A495AD750CE525CB57F728FD453091B3E092240B0A1A8089425FBAE61AA6E")]
    public void Decode_ReproducesTheCapturedTexels(string file, int width, int height, string sha256)
    {
        using Stream stream = File.OpenRead(Fixture(file));

        using DecodedTexture decoded = TextureDecoder.Decode(stream, file);

        Assert.Equal((width, height), (decoded.Width, decoded.Height));
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(decoded.Texels)));
    }

    // The build packs atlas pages from stb's decode, and a texture that ships unpacked decodes here.
    // Both must hold the same texels: stb's RGBA premultiplied by truncation, or an r8 file's grey.
    // The 13-texel-wide RGB file decodes to rows SDL pads to no multiple of four.
    [Theory]
    [InlineData("alpha-ramp.png", false)]
    [InlineData("hd-atlas.png", false)]
    [InlineData("rgba", false)]
    [InlineData("rgb", false)]
    [InlineData("rgb with a transparent colour", false)]
    [InlineData("grey", false)]
    [InlineData("grey at 4 bits", false)]
    [InlineData("grey with alpha", false)]
    [InlineData("palette at 1 bit", false)]
    [InlineData("palette at 4 bits with alpha", false)]
    [InlineData("palette at 8 bits with alpha", false)]
    [InlineData("grey", true)]
    [InlineData("grey at 4 bits", true)]
    public void Decode_MatchesTheBuildsDecodeOfEveryKindOfPng(string kind, bool singleChannel)
    {
        byte[] png = kind.EndsWith(".png", StringComparison.Ordinal) ? File.ReadAllBytes(Fixture(kind)) : Generated(kind);
        ImageResult reference = ImageResult.FromMemory(png, singleChannel ? ColorComponents.Grey : ColorComponents.RedGreenBlueAlpha);
        if (!singleChannel)
        {
            for (int index = 0; index < reference.Data.Length; index += 4)
            {
                float scale = reference.Data[index + 3] / 255f;
                reference.Data[index] = (byte)(reference.Data[index] * scale);
                reference.Data[index + 1] = (byte)(reference.Data[index + 1] * scale);
                reference.Data[index + 2] = (byte)(reference.Data[index + 2] * scale);
            }
        }

        using DecodedTexture decoded = TextureDecoder.Decode(new MemoryStream(png), kind, singleChannel);

        Assert.Equal((reference.Width, reference.Height, singleChannel ? 1 : 4), (decoded.Width, decoded.Height, decoded.BytesPerTexel));
        Assert.True(reference.Data.AsSpan().SequenceEqual(decoded.Texels), $"'{kind}' decodes to texels stb does not.");
    }

    [Fact]
    public void Decode_RefusesAMalformedFileNamingTheTextureAndTheFix()
    {
        byte[] png = Generated("rgba");

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => TextureDecoder.Decode(new MemoryStream(png[..(png.Length / 2)]), "hero"));

        Assert.StartsWith("Texture 'hero' is not a PNG the decoder reads (", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("Export it again as a PNG.", error.Message, StringComparison.Ordinal);
    }

    // A decode the scene stops wanting while it runs finishes, then destroys its surface.
    [Fact]
    public void ADecodeDroppedInFlight_DestroysItsSurface()
    {
        using ManualResetEventSlim running = new();
        using ManualResetEventSlim release = new();
        DecodedTexture? decoded = null;
        using SceneAssetStore<TextureHandle, SampledTexture> store = new(
            _ =>
            {
                running.Set();
                release.Wait();
                decoded = TextureDecoder.Decode(new MemoryStream(Generated("rgba")), "dropped");
                return new TextureStore.TextureUpload(null!, decoded);
            },
            uploadBudget: 0);

        store.Prefetch([new TextureHandle("dropped", ".png")]);
        Assert.True(running.Wait(TimeSpan.FromSeconds(10)));
        store.Prefetch([]);
        release.Set();

        Assert.True(SpinWait.SpinUntil(() => decoded is { Released: true }, TimeSpan.FromSeconds(10)));
        Assert.Throws<ObjectDisposedException>(() => decoded!.Texels.Length);
    }

    private static string Fixture(string file) => Path.Combine(AppContext.BaseDirectory, "Runtime", "Fixtures", file);

    // Generated files at every colour type and the bit depths the build's authors export.
    private static byte[] Generated(string kind)
    {
        byte[] values = SingleChannelPngTests.Values(8);
        byte[] palette = [.. Enumerable.Range(0, 256 * 3).Select(static i => (byte)((i * 97) + 13))];
        byte[] alphas = [.. Enumerable.Range(0, 200).Select(static i => (byte)(i * 37))];
        byte[] rgb = [.. values.SelectMany(static v => new[] { v, (byte)(255 - v), (byte)(v * 3) })];
        return kind switch
        {
            "rgba" => SingleChannelPngTests.Png([.. values.SelectMany(static v => new[] { v, (byte)(255 - v), (byte)(v * 3), (byte)(v * 5) })], 6, 8, null, channels: 4),
            "rgb" => SingleChannelPngTests.Png(rgb, 2, 8, null, channels: 3),

            // The colour of a value of 0 is transparent.
            "rgb with a transparent colour" => SingleChannelPngTests.Png(rgb, 2, 8, null, channels: 3, transparency: [0, 0, 0, 255, 0, 0]),
            "grey" => SingleChannelPngTests.Png(values, 0, 8, null),
            "grey at 4 bits" => SingleChannelPngTests.Png(SingleChannelPngTests.Values(4), 0, 4, null),
            "grey with alpha" => SingleChannelPngTests.Png([.. values.SelectMany(static v => new[] { v, (byte)(v * 7) })], 4, 8, null, channels: 2),
            "palette at 1 bit" => SingleChannelPngTests.Png(SingleChannelPngTests.Values(1), 3, 1, palette[..6]),
            "palette at 4 bits with alpha" => SingleChannelPngTests.Png(SingleChannelPngTests.Values(4), 3, 4, palette[..48], transparency: alphas[..10]),

            // Indices past the last alpha are opaque.
            "palette at 8 bits with alpha" => SingleChannelPngTests.Png(values, 3, 8, palette, transparency: alphas),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such generated PNG."),
        };
    }
}
