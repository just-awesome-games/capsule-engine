using Capsule.Build.Textures;
using StbImageSharp;

namespace Capsule.Tests.Build;

/// <summary>The build's PNG encoder against stb's decode, at both channel counts it writes.</summary>
public sealed class PngWriterTests
{
    // Noise rows beside smooth rows, so each row filter wins somewhere.
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AnEncodedPng_DecodesToExactlyItsTexels(int channels)
    {
        const int Width = 37;
        const int Height = 24;
        byte[] texels = new byte[Width * Height * channels];
        Random random = new(7);
        for (int y = 0; y < Height; y++)
        {
            for (int i = 0; i < Width * channels; i++)
            {
                texels[(y * Width * channels) + i] = (y % 3) switch
                {
                    0 => (byte)random.Next(256),
                    1 => (byte)(i * 3),
                    _ => (byte)((i / channels) + y),
                };
            }
        }

        using MemoryStream png = new();
        TexturePixels.Encode(texels, Width, Height, png, channels);
        ImageResult decoded = ImageResult.FromMemory(png.ToArray(), channels == 1 ? ColorComponents.Grey : ColorComponents.RedGreenBlueAlpha);

        Assert.Equal((Width, Height), (decoded.Width, decoded.Height));
        Assert.Equal(texels, decoded.Data);
    }
}
