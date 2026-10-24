using Capsule.Assets;
using StbImageSharp;

namespace Capsule.Build.Textures;

/// <summary>A texture's texels in its format, decoded and copied.</summary>
internal static class TexturePixels
{
    /// <summary>The bytes a texel of <paramref name="format"/> ships in.</summary>
    internal static int Channels(TextureFormatSetting format) => format == TextureFormatSetting.R8 ? 1 : 4;

    /// <summary>A texture's texels in its format: RGBA8 as authored, or an r8 texture's one channel.</summary>
    /// <exception cref="FormatException">The file cannot be decoded, or an r8 source is of a kind with no single channel.</exception>
    internal static Texels Decode(string path, TextureFormatSetting format)
    {
        byte[] file = File.ReadAllBytes(path);
        if (format == TextureFormatSetting.R8)
        {
            return SingleChannelPng.Read(file);
        }

        ImageResult image;
        try
        {
            image = ImageResult.FromMemory(file, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            // How the decoder fails, as one defect of the file.
            throw new FormatException($"is not a PNG the build can decode: {ex.Message.TrimEnd('.')}. Export it again as a PNG.", ex);
        }

        return new Texels(image.Data, image.Width, image.Height, 4);
    }

    /// <summary>
    /// Scales RGBA <paramref name="source"/> to <paramref name="width"/> by <paramref name="height"/>,
    /// each texel the average of the source area it covers, weighted by alpha.
    /// </summary>
    /// <remarks>
    /// A transparent texel lends its neighbours no colour. A whole-number ratio averages whole blocks,
    /// and a block of one colour stays exactly that colour. The arithmetic is integer and exact.
    /// </remarks>
    internal static Texels Downscale(Texels source, int width, int height)
    {
        // Coverage is counted in units of 1/width of a source column and 1/height of a source row. In
        // those units every weight is whole, and a target texel's weights sum to the source's area.
        (int First, int[] Weights)[] columns = Coverage(source.Width, width);
        (int First, int[] Weights)[] rows = Coverage(source.Height, height);
        long area = (long)source.Width * source.Height;
        byte[] texels = source.Data;
        byte[] data = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            (int top, int[] rowWeights) = rows[y];
            for (int x = 0; x < width; x++)
            {
                (int left, int[] columnWeights) = columns[x];
                long red = 0;
                long green = 0;
                long blue = 0;
                long alpha = 0;
                for (int j = 0; j < rowWeights.Length; j++)
                {
                    int line = (top + j) * source.Width;
                    for (int i = 0; i < columnWeights.Length; i++)
                    {
                        int at = (line + left + i) * 4;
                        long weight = (long)rowWeights[j] * columnWeights[i] * texels[at + 3];
                        red += weight * texels[at];
                        green += weight * texels[at + 1];
                        blue += weight * texels[at + 2];
                        alpha += weight;
                    }
                }

                // Straight alpha again. A texel nothing opaque covers stays transparent black.
                int target = ((y * width) + x) * 4;
                if (alpha > 0)
                {
                    data[target] = (byte)Rounded(red, alpha);
                    data[target + 1] = (byte)Rounded(green, alpha);
                    data[target + 2] = (byte)Rounded(blue, alpha);
                    data[target + 3] = (byte)Rounded(alpha, area);
                }
            }
        }

        return new Texels(data, width, height, 4);
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

    // For each of the target's texels along one axis, the first source texel it covers and how much
    // of each one from there, in units of 1/target of a source texel.
    private static (int First, int[] Weights)[] Coverage(int source, int target)
    {
        (int, int[])[] coverage = new (int, int[])[target];
        for (int t = 0; t < target; t++)
        {
            long start = (long)t * source;
            long end = start + source;
            int first = (int)(start / target);
            int last = (int)((end - 1) / target);
            int[] weights = new int[last - first + 1];
            for (int i = first; i <= last; i++)
            {
                weights[i - first] = (int)(Math.Min((long)(i + 1) * target, end) - Math.Max((long)i * target, start));
            }

            coverage[t] = (first, weights);
        }

        return coverage;
    }

    private static long Rounded(long numerator, long denominator) => (numerator + (denominator / 2)) / denominator;
}

/// <summary>Texels in rows, <paramref name="Channels"/> bytes each and no padding.</summary>
internal readonly record struct Texels(byte[] Data, int Width, int Height, int Channels);
