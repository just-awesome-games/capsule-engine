using Capsule.Build.Atlases;
using StbTrueTypeSharp;

namespace Capsule.Build.Fonts;

// Rasterizes faces at one size into a bitmap font's pages, glyphs and kerning. One input bakes the same
// texels on every machine: the rasterizer is managed code with no hinting, and the packer is deterministic.
internal static unsafe class FontRasterizer
{
    /// <summary>The largest extent a page reaches on either axis.</summary>
    internal const int PageSize = 2048;

    // Kerning is read between Latin letters alone. Every pair of a large set costs its square in lookups,
    // and the scripts beyond it are rarely kerned.
    private const int LastKerned = 'ÿ';

    internal sealed record Baked(int LineHeight, int Baseline, int PageWidth, int PageHeight, byte[][] Pages, BmGlyph[] Glyphs, BmKerning[] Kernings);

    // One glyph's texels before packing: its codepoint, extent, offsets from the pen, advance, and the face
    // and glyph index that kern it.
    private readonly record struct Cut(int Codepoint, int Width, int Height, int XOffset, int YOffset, int XAdvance, byte[] Texels, int Face, int Index);

    /// <summary>Bakes each of <paramref name="codepoints"/> the first face carrying it has.</summary>
    /// <param name="faces">Each face's name for a refusal and its file, the first setting line spacing.</param>
    /// <param name="size">The em in pixels.</param>
    /// <param name="antialias">Whether a texel keeps its coverage, or is whole at half coverage or more.</param>
    /// <param name="codepoints">The codepoints to bake, ascending with no repeat. A codepoint no face carries is left out.</param>
    /// <exception cref="FormatException">A face is no font the rasterizer reads, or a glyph is larger than a page.</exception>
    internal static Baked Bake(IReadOnlyList<(string Name, byte[] Data)> faces, int size, bool antialias, IReadOnlyList<int> codepoints)
    {
        StbTrueType.stbtt_fontinfo[] infos = new StbTrueType.stbtt_fontinfo[faces.Count];
        try
        {
            float[] scales = new float[faces.Count];
            for (int face = 0; face < faces.Count; face++)
            {
                if (FaceCheck.Defect(faces[face].Data) is { } defect)
                {
                    throw new FormatException($"names the face \"{faces[face].Name}\", which {defect}");
                }

                infos[face] = StbTrueType.CreateFont(faces[face].Data, 0)
                    ?? throw new FormatException($"names the face \"{faces[face].Name}\", which is no TrueType or OpenType font. Name a .ttf or .otf file.");
                scales[face] = StbTrueType.stbtt_ScaleForMappingEmToPixels(infos[face], size);
            }

            int ascent;
            int descent;
            int gap;
            StbTrueType.stbtt_GetFontVMetrics(infos[0], &ascent, &descent, &gap);
            int baseline = (int)MathF.Ceiling(ascent * scales[0]);
            int lineHeight = baseline + (int)MathF.Ceiling(-descent * scales[0]) + (int)MathF.Round(gap * scales[0], MidpointRounding.AwayFromZero);

            List<Cut> cuts = new(codepoints.Count);
            foreach (int codepoint in codepoints)
            {
                for (int face = 0; face < infos.Length; face++)
                {
                    int index = StbTrueType.stbtt_FindGlyphIndex(infos[face], codepoint);
                    if (index != 0)
                    {
                        cuts.Add(Render(infos[face], scales[face], face, index, codepoint, baseline, antialias));
                        break;
                    }
                }
            }

            return Packed(cuts, lineHeight, baseline, Kernings(infos, scales, cuts));
        }
        finally
        {
            foreach (StbTrueType.stbtt_fontinfo? info in infos)
            {
                info?.Dispose();
            }
        }
    }

    private static Cut Render(StbTrueType.stbtt_fontinfo info, float scale, int face, int index, int codepoint, int baseline, bool antialias)
    {
        int advance;
        int bearing;
        StbTrueType.stbtt_GetGlyphHMetrics(info, index, &advance, &bearing);
        int x0;
        int y0;
        int x1;
        int y1;
        StbTrueType.stbtt_GetGlyphBitmapBox(info, index, scale, scale, &x0, &y0, &x1, &y1);
        int width = x1 - x0;
        int height = y1 - y0;
        byte[] texels = new byte[width * height];
        if (texels.Length > 0)
        {
            fixed (byte* output = texels)
            {
                StbTrueType.stbtt_MakeGlyphBitmap(info, output, width, height, width, scale, scale, index);
            }

            if (!antialias)
            {
                for (int i = 0; i < texels.Length; i++)
                {
                    texels[i] = texels[i] >= 128 ? byte.MaxValue : byte.MinValue;
                }
            }
        }

        int xAdvance = (int)MathF.Round(advance * scale, MidpointRounding.AwayFromZero);

        return new Cut(codepoint, width, height, x0, baseline + y0, xAdvance, texels, face, index);
    }

    // Each pair of Latin glyphs from one face that the face kerns by a whole pixel or more.
    private static List<BmKerning> Kernings(StbTrueType.stbtt_fontinfo[] infos, float[] scales, List<Cut> cuts)
    {
        List<BmKerning> kernings = [];
        foreach (Cut first in cuts)
        {
            if (first.Codepoint > LastKerned)
            {
                continue;
            }

            foreach (Cut second in cuts)
            {
                if (second.Codepoint > LastKerned || second.Face != first.Face)
                {
                    continue;
                }

                int units = StbTrueType.stbtt_GetGlyphKernAdvance(infos[first.Face], first.Index, second.Index);
                int amount = (int)MathF.Round(units * scales[first.Face], MidpointRounding.AwayFromZero);
                if (amount != 0)
                {
                    kernings.Add(new BmKerning(first.Codepoint, second.Codepoint, amount));
                }
            }
        }

        return kernings;
    }

    // Packs every cut with texels onto pages of one extent, the largest any page needs. An empty cut, as a
    // space, takes no texels and sits at the first page's corner.
    private static Baked Packed(List<Cut> cuts, int lineHeight, int baseline, List<BmKerning> kernings)
    {
        List<(string Key, int Width, int Height)> cells = [];
        foreach (Cut cut in cuts)
        {
            if (cut.Width > PageSize || cut.Height > PageSize)
            {
                throw new FormatException($"draws codepoint {cut.Codepoint} {cut.Width}x{cut.Height} pixels, larger than a {PageSize}-pixel page. Bake a smaller size.");
            }

            if (cut.Width > 0 && cut.Height > 0)
            {
                cells.Add((Key(cut.Codepoint), cut.Width, cut.Height));
            }
        }

        (Placement[] placements, (int Width, int Height)[] extents) = cells.Count > 0
            ? AtlasPacker.Pack(cells, PageSize)
            : ([], [(1, 1)]);
        int pageWidth = extents.Max(static extent => extent.Width);
        int pageHeight = extents.Max(static extent => extent.Height);
        byte[][] pages = [.. extents.Select(_ => new byte[pageWidth * pageHeight])];
        Dictionary<string, Placement> placed = placements.ToDictionary(static placement => placement.Key, StringComparer.Ordinal);

        BmGlyph[] glyphs = new BmGlyph[cuts.Count];
        for (int i = 0; i < cuts.Count; i++)
        {
            Cut cut = cuts[i];
            Placement at = placed.TryGetValue(Key(cut.Codepoint), out Placement found) ? found : new Placement(string.Empty, 0, 0, 0);
            for (int row = 0; row < cut.Height; row++)
            {
                cut.Texels.AsSpan(row * cut.Width, cut.Width).CopyTo(pages[at.Page].AsSpan(((at.Y + row) * pageWidth) + at.X));
            }

            glyphs[i] = new BmGlyph(cut.Codepoint, at.Page, at.X, at.Y, cut.Width, cut.Height, cut.XOffset, cut.YOffset, cut.XAdvance);
        }

        return new Baked(lineHeight, baseline, pageWidth, pageHeight, pages, glyphs, [.. kernings]);
    }

    private static string Key(int codepoint) => codepoint.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
