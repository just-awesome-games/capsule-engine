using System.Globalization;
using System.Text;
using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

// Renders every sheet a game authors as typed members of CapsuleAssets.Sprites, each frame a Sprite
// literal and each clip one shared SpriteClip, so a misspelt frame or clip is a compile error and
// no sheet ships beside the executable.
internal static class SpriteRegistrySource
{
    internal const string RegistryClass = "Sprites";

    internal const string Domain = "sprites";

    /// <summary>The generated class a sheet's frames are declared on.</summary>
    internal const string FramesClass = "Frames";

    /// <summary>The generated class a sheet's clips are declared on.</summary>
    internal const string ClipsClass = "Clips";

    private const string SpriteType = "global::Capsule.Rendering.Sprite";

    private const string RegionType = "global::Capsule.Rendering.TextureRegion";

    private const string TextureType = "global::Capsule.Assets.TextureHandle";

    private const string VectorType = "global::System.Numerics.Vector2";

    private const string ClipType = "global::Capsule.Animation.SpriteClip";

    /// <summary>Reads one sheet additional file into the model the registry is built from.</summary>
    internal static ParsedAsset<SheetDocument> Describe(
        AdditionalText text,
        string authored,
        CancellationToken cancellation) =>
        ParsedAsset.Describe<SheetDocument>(
            text,
            authored,
            Domain,
            SheetJsonReader.SheetExtension,
            "cannot be read as text; a sheet document is UTF-8 JSON.",
            SheetJsonReader.Parse,
            cancellation);

    /// <summary>Whether <paramref name="identifier"/> is a name a sheet's own classes take.</summary>
    internal static bool Reserves(string identifier, bool leaf) =>
        leaf && identifier is FramesClass or ClipsClass;

    // No 'All' set: a sheet is a pair of classes rather than one member, so the domain has no single
    // type to hand a span of.
    internal static RegistryDomain<SheetDocument> Registry() =>
        new(
            RegistryClass,
            Domain,
            memberType: null,
            noun: "sheet",
            "Every sprite sheet this game authors, as frames and clips.",
            AppendSheet);

    private static void AppendSheet(StringBuilder source, string indent, string identifier, SheetDocument document)
    {
        string texture = $"new {TextureType}({Literal(document.TextureKey)}, {Literal(document.TextureExtension)})";
        string frames = indent + "    ";
        string member = frames + "    ";

        source.Append(indent).Append("/// <summary>The ")
            .Append(document.Clips.Length == 0 ? "frames" : "frames and clips").Append(" cut from <c>")
            .Append(document.Texture).AppendLine("</c>.</summary>");
        source.Append(indent).Append("public static class ").AppendLine(identifier);
        source.Append(indent).AppendLine("{");

        source.Append(frames).AppendLine("/// <summary>Every frame this sheet cuts, as sprites.</summary>");
        source.Append(frames).Append("public static class ").AppendLine(FramesClass);
        source.Append(frames).AppendLine("{");

        for (int i = 0; i < document.Frames.Length; i++)
        {
            SheetFrame frame = document.Frames[i];
            if (i > 0)
            {
                source.AppendLine();
            }

            source.Append(member).Append("/// <summary><c>").Append(frame.Name).Append("</c>: ")
                .Append(Number(frame.Width)).Append('x').Append(Number(frame.Height))
                .Append(" at (").Append(Number(frame.X)).Append(", ").Append(Number(frame.Y))
                .AppendLine(").</summary>");
            source.Append(member).Append("public static ").Append(SpriteType).Append(' ')
                .Append(Identifier(frame.Name)).Append(" => new ").Append(SpriteType).AppendLine("(");
            source.Append(member).Append("    ").Append(texture).AppendLine(",");
            source.Append(member).Append("    new ").Append(RegionType).Append('(')
                .Append(Number(frame.X)).Append(", ").Append(Number(frame.Y)).Append(", ")
                .Append(Number(frame.Width)).Append(", ").Append(Number(frame.Height)).AppendLine("),");
            source.Append(member).Append("    new ").Append(VectorType).Append('(')
                .Append(Number(frame.PivotX)).Append(", ").Append(Number(frame.PivotY)).AppendLine("));");
        }

        source.Append(frames).AppendLine("}");

        // No empty class on a sheet of frames only: a consumer naming Clips is then a compile error
        // rather than a member that never resolves.
        if (document.Clips.Length == 0)
        {
            source.Append(indent).AppendLine("}");

            return;
        }

        source.AppendLine();
        source.Append(frames).AppendLine("/// <summary>Every clip this sheet plays over those frames.</summary>");
        source.Append(frames).Append("public static class ").AppendLine(ClipsClass);
        source.Append(frames).AppendLine("{");

        for (int i = 0; i < document.Clips.Length; i++)
        {
            SheetClip clip = document.Clips[i];
            if (i > 0)
            {
                source.AppendLine();
            }

            source.Append(member).Append("/// <summary><c>").Append(clip.Name).Append("</c>: ")
                .Append(Number(clip.Frames.Length)).Append(clip.Frames.Length == 1 ? " frame, " : " frames, ")
                .Append(Number(TotalTicks(clip))).Append(" ticks, ")
                .Append(clip.Loop ? "looping" : "played once").AppendLine(".</summary>");

            // A property with a field initializer, not an expression body: a clip is immutable, so
            // every entity playing it reads one instance.
            source.Append(member).Append("public static ").Append(ClipType).Append(' ')
                .Append(Identifier(clip.Name)).Append(" { get; } = new ").Append(ClipType).AppendLine("(");
            source.Append(member).Append("    new ").Append(SpriteType).Append("[] { ");
            for (int j = 0; j < clip.Frames.Length; j++)
            {
                if (j > 0)
                {
                    source.Append(", ");
                }

                source.Append(FramesClass).Append('.').Append(Identifier(clip.Frames[j].Frame));
            }

            source.AppendLine(" },");
            source.Append(member).Append("    new int[] { ");
            for (int j = 0; j < clip.Frames.Length; j++)
            {
                if (j > 0)
                {
                    source.Append(", ");
                }

                source.Append(Number(clip.Frames[j].Ticks));
            }

            source.Append(" },").AppendLine();
            source.Append(member).Append("    ").Append(clip.Loop ? "true" : "false").AppendLine(");");
        }

        source.Append(frames).AppendLine("}");
        source.Append(indent).AppendLine("}");
    }

    private static int TotalTicks(SheetClip clip)
    {
        int total = 0;
        foreach (SheetClipFrame frame in clip.Frames)
        {
            total += frame.Ticks;
        }

        return total;
    }

    // The document has already been validated, so a name that is no identifier cannot reach here.
    private static string Identifier(string name) => TypeNaming.ToIdentifier(name)!;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    // Round-trippable and suffixed: the pivot is a float, and an unsuffixed literal would be a
    // double the constructor cannot take.
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "F";

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
}
