using Capsule.Assets;
using StbImageSharp;

namespace Capsule.Build.Textures;

/// <summary>A texture's texels in its format, decoded, copied and encoded as PNG.</summary>
internal static class TexturePixels
{
    /// <summary>The bytes a texel of <paramref name="format"/> ships in.</summary>
    internal static int Channels(TextureFormatSetting format) => format == TextureFormatSetting.R8 ? 1 : 4;

    /// <summary>Encodes texels as a PNG into <paramref name="destination"/>, straight-alpha RGBA8 at four channels and 8-bit greyscale at one.</summary>
    internal static void Encode(byte[] texels, int width, int height, Stream destination, int channels = 4) =>
        PngWriter.Write(texels, width, height, channels, destination);

    /// <summary>A texture's texels in its format: RGBA8 as authored, or an r8 texture's one channel.</summary>
    /// <exception cref="FormatException">The file cannot be read or decoded, or an r8 source is of a kind with no single channel.</exception>
    internal static Texels Decode(string path, TextureFormatSetting format)
    {
        try
        {
            byte[] file = File.ReadAllBytes(path);
            if (Channels(format) == 1)
            {
                return SingleChannelPng.Read(file);
            }

            ImageResult image = ImageResult.FromMemory(file, ColorComponents.RedGreenBlueAlpha);

            return new Texels(image.Data, image.Width, image.Height, 4);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or IOException or UnauthorizedAccessException)
        {
            // How the decoder and the disk fail, as one defect of the file.
            throw new FormatException($"is not a PNG the packer can decode: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Copies <paramref name="member"/> onto <paramref name="page"/> with its top-left texel at
    /// (<paramref name="x"/>, <paramref name="y"/>) and <paramref name="border"/> texels of its edge
    /// repeated outward on every side, corners included.
    /// </summary>
    /// <param name="pageWidth">The page's width in texels, each of the member's channel count.</param>
    internal static void Blit(byte[] page, int pageWidth, Texels member, int x, int y, int border)
    {
        int width = member.Width;
        int height = member.Height;
        int texel = member.Channels;
        ReadOnlySpan<byte> source = member.Data;

        for (int row = -border; row < height + border; row++)
        {
            ReadOnlySpan<byte> line = source.Slice(Math.Clamp(row, 0, height - 1) * width * texel, width * texel);
            Span<byte> target = page.AsSpan((((y + row) * pageWidth) + x - border) * texel);

            for (int i = 0; i < border; i++)
            {
                line[..texel].CopyTo(target.Slice(i * texel, texel));
                line[^texel..].CopyTo(target.Slice((border + width + i) * texel, texel));
            }

            line.CopyTo(target.Slice(border * texel, width * texel));
        }
    }
}
