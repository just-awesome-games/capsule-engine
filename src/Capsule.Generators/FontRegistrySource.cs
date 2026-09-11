using System.Globalization;
using System.Text;
using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal enum FontFault
{
    None,
    UnsafeName,
    Unreadable,
}

// One authored '.fnt' as the generator read it. Description is compared by reference: a re-parse
// regenerates the registry whether or not the bytes changed, which is conservative, never stale.
internal readonly struct FontModel(
    string key,
    string display,
    FontFault fault,
    string? message,
    BmFontDescription? description,
    Location location)
    : IEquatable<FontModel>
{
    /// <summary>The source's key under the fonts root, or its authored path when that is no key.</summary>
    internal string Key { get; } = key;

    /// <summary>What a diagnostic names the font by: its path under the source tree.</summary>
    internal string Display { get; } = display;

    internal FontFault Fault { get; } = fault;

    /// <summary>Why the font could not be read, when it could not.</summary>
    internal string? Message { get; } = message;

    internal BmFontDescription? Description { get; } = description;

    /// <summary>The '.fnt' itself, at the line the defect is on: what a build error navigates to.</summary>
    internal Location Location { get; } = location;

    public bool Equals(FontModel other) =>
        Fault == other.Fault
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(Display, other.Display, StringComparison.Ordinal)
        && string.Equals(Message, other.Message, StringComparison.Ordinal)
        && ReferenceEquals(Description, other.Description)
        && Location.Equals(other.Location);

    public override bool Equals(object? obj) => obj is FontModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + Key.GetHashCode();
        hash = (hash * 31) + Display.GetHashCode();
        hash = (hash * 31) + (int)Fault;

        return hash;
    }
}

// One shipped font, with every page resolved to the key and spelling the build ships it at.
internal sealed class FontSource(string key, BmFontDescription description, string[] pageKeys, string[] pageExtensions)
{
    internal string Key { get; } = key;

    internal BmFontDescription Description { get; } = description;

    internal string[] PageKeys { get; } = pageKeys;

    internal string[] PageExtensions { get; } = pageExtensions;
}

// Renders every font a game ships as typed members of CapsuleAssets.Fonts, each carrying the
// metrics, glyphs and kerning read out of its '.fnt', so a misspelt font is a compile error,
// nothing is parsed at run time, and the '.fnt' itself never ships — only the pages it names.
internal static class FontRegistrySource
{
    internal const string RegistryClass = "Fonts";

    internal const string Domain = "fonts";

    private const string FontType = "global::Capsule.Rendering.BitmapFont";

    private const string HandleType = "global::Capsule.Assets.TextureHandle";

    private const string GlyphType = "global::Capsule.Rendering.Glyph";

    private const string RegionType = "global::Capsule.Rendering.TextureRegion";

    private const string KerningType = "global::Capsule.Rendering.KerningPair";

    /// <summary>Reads one <c>.fnt</c> additional file into the model the registry is built from.</summary>
    internal static FontModel Describe(AdditionalText text, string authored, CancellationToken cancellation)
    {
        string display = Domain + "/" + authored + BmFontParser.BmFontExtension;
        SourceText? content = text.GetText(cancellation);

        if (TypeNaming.NormalizeKey(authored, out _) is not { } key)
        {
            return new FontModel(authored, display, FontFault.UnsafeName, null, null, At(text.Path, content, 0));
        }

        if (!AssetPaths.IsKey(key))
        {
            return new FontModel(
                key,
                display,
                FontFault.Unreadable,
                $"keys as \"{key}\"; a segment of a key is no reserved Windows device name (nul, con, ...).",
                null,
                At(text.Path, content, 0));
        }

        if (content is null)
        {
            return new FontModel(
                key,
                display,
                FontFault.Unreadable,
                "cannot be read as text; export the font as text.",
                null,
                At(text.Path, null, 0));
        }

        BmFontDescription? described = BmFontParser.Parse(content.ToString(), out string? error, out int line);

        return described is null
            ? new FontModel(key, display, FontFault.Unreadable, error, null, At(text.Path, content, line))
            : new FontModel(key, display, FontFault.None, null, described, At(text.Path, content, 0));
    }

    // The '.fnt' as a location a build error navigates to. The compiler holds no syntax tree for an
    // additional file, so the span is spelled out against the file itself.
    private static Location At(string path, SourceText? content, int line)
    {
        if (content is null || line >= content.Lines.Count)
        {
            return Location.Create(path, new TextSpan(0, 0), new LinePositionSpan(default, default));
        }

        TextLine text = content.Lines[line];

        return Location.Create(
            path,
            text.Span,
            new LinePositionSpan(new LinePosition(line, 0), new LinePosition(line, text.Span.Length)));
    }

    /// <summary>
    /// Where each page ships. A page is a fonts-domain asset of its own, so the key pass has already
    /// keyed it off its authored path; deriving that same key here from the font's key and the
    /// page's file name is what ties the two together across normalization. A page missing from
    /// <paramref name="shipped"/> is one the build does not ship — excluded as development-only, or
    /// never authored — and a handle naming it would find no file at run time.
    /// </summary>
    /// <returns>The source, or null with <paramref name="error"/> set.</returns>
    internal static FontSource? Resolve(
        string key,
        BmFontDescription description,
        IReadOnlyDictionary<string, string> shipped,
        out string? error)
    {
        int slash = key.LastIndexOf('/');
        string directory = slash < 0 ? string.Empty : key.Substring(0, slash + 1);
        string[] keys = new string[description.PageFiles.Length];
        string[] extensions = new string[description.PageFiles.Length];

        for (int i = 0; i < description.PageFiles.Length; i++)
        {
            string file = description.PageFiles[i];
            string stem = file.Substring(0, file.Length - Path.GetExtension(file).Length);

            if (TypeNaming.NormalizeKey(stem, out string? rejected) is not { } normalized)
            {
                error = $"names page \"{file}\", whose \"{rejected}\" is no C# name; every segment of a path under a domain root is letters, digits, '-' and '_', and does not start with a digit.";

                return null;
            }

            string page = directory + normalized;

            // The shipped spelling, not the one the font used: the handle has to name the file the
            // build actually wrote.
            if (!shipped.TryGetValue(page, out string? extension))
            {
                error = $"names page \"{file}\", which this game ships nothing for at \"assets/fonts/{page}\"; author the page under Assets/Fonts/ and keep it out of a development-only directory.";

                return null;
            }

            keys[i] = page;
            extensions[i] = extension;
        }

        error = null;

        return new FontSource(key, description, keys, extensions);
    }

    internal static RegistryDomain<FontSource> Registry() =>
        new(
            RegistryClass,
            Domain,
            FontType,
            "font",
            "Every font this game ships, with the glyphs the build read for it.",
            AppendFont);

    // One instance, built once from literal data: a font is a class, so the member hands the same
    // one back rather than rebuilding its tables per call.
    private static void AppendFont(StringBuilder source, string indent, string identifier, FontSource font)
    {
        BmFontDescription described = font.Description;
        string field = "_font" + identifier;
        string body = indent + "    ";

        source.Append(indent).Append("private static readonly ").Append(FontType).Append(' ').Append(field)
            .Append(" = new ").Append(FontType).AppendLine("(");
        source.Append(body).Append(Number(described.LineHeight)).AppendLine(",");
        source.Append(body).Append(Number(described.Base)).AppendLine(",");

        source.Append(body).Append("new ").Append(HandleType).AppendLine("[]");
        source.Append(body).AppendLine("{");
        for (int i = 0; i < font.PageKeys.Length; i++)
        {
            source.Append(body).Append("    ").Append(HandleType).Append(".FontPage(").Append(Literal(font.PageKeys[i]))
                .Append(", ").Append(Literal(font.PageExtensions[i])).AppendLine("),");
        }

        source.Append(body).AppendLine("},");

        source.Append(body).Append("new ").Append(GlyphType).AppendLine("[]");
        source.Append(body).AppendLine("{");
        foreach (BmGlyph glyph in described.Glyphs)
        {
            source.Append(body).Append("    new ").Append(GlyphType).Append('(').Append(Number(glyph.Codepoint))
                .Append(", ").Append(Number(glyph.Page)).Append(", new ").Append(RegionType).Append('(')
                .Append(Number(glyph.X)).Append(", ").Append(Number(glyph.Y)).Append(", ")
                .Append(Number(glyph.Width)).Append(", ").Append(Number(glyph.Height)).Append("), ")
                .Append(Number(glyph.XOffset)).Append(", ").Append(Number(glyph.YOffset)).Append(", ")
                .Append(Number(glyph.XAdvance)).AppendLine("),");
        }

        source.Append(body).AppendLine("},");

        source.Append(body).Append("new ").Append(KerningType).AppendLine("[]");
        source.Append(body).AppendLine("{");
        foreach (BmKerning kerning in described.Kernings)
        {
            source.Append(body).Append("    new ").Append(KerningType).Append('(').Append(Number(kerning.First))
                .Append(", ").Append(Number(kerning.Second)).Append(", ").Append(Number(kerning.Amount))
                .AppendLine("),");
        }

        source.Append(body).AppendLine("});");
        source.AppendLine();

        source.Append(indent).Append("/// <summary><c>").Append(Domain).Append('/').Append(font.Key)
            .Append(BmFontParser.BmFontExtension).Append("</c>, ").Append(Number(described.LineHeight))
            .Append(" pixels per line, ").Append(Number(described.Glyphs.Length)).Append(" glyph(s) over ")
            .Append(Number(font.PageKeys.Length)).AppendLine(" page(s).</summary>");
        source.Append(indent).Append("public static ").Append(FontType).Append(' ').Append(identifier)
            .Append(" => ").Append(field).AppendLine(";");
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
