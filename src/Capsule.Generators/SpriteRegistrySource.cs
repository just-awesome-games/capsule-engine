using System.Globalization;
using System.Text;
using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

// Renders every sheet a game authors as typed members of CapsuleAssets.Sprites, each frame a Sprite
// literal over one shared socket table, each clip one shared SpriteClip and each socket a name
// constant, so a misspelt frame, clip or socket is a compile error and no sheet ships beside the
// executable.
internal static class SpriteRegistrySource
{
    internal const string RegistryClass = "Sprites";

    internal const string Domain = "sprites";

    /// <summary>The generated class a sheet's frames are declared on.</summary>
    internal const string FramesClass = "Frames";

    /// <summary>The generated class a sheet's clips are declared on.</summary>
    internal const string ClipsClass = "Clips";

    /// <summary>The generated class a sheet's socket names are declared on.</summary>
    internal const string SocketsClass = "Sockets";

    private const string SocketType = "global::Capsule.Rendering.SpriteSocket";

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
        leaf && identifier is FramesClass or ClipsClass or SocketsClass;

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

            // One table per frame, read by every Sprite the property hands out: two reads are then
            // equal and neither allocates. The field's underscore is what no frame identifier
            // carries, so it collides with no frame.
            string table = Identifier(frame.Name) + "_Sockets";
            if (frame.Sockets.Length > 0)
            {
                source.Append(member).Append("private static readonly ").Append(SocketType).Append("[] ")
                    .Append(table).AppendLine(" =");
                source.Append(member).Append('{');
                for (int j = 0; j < frame.Sockets.Length; j++)
                {
                    SheetSocket socket = frame.Sockets[j];
                    source.AppendLine().Append(member).Append("    new ").Append(SocketType).Append('(')
                        .Append(Literal(socket.Name)).Append(", new ").Append(VectorType).Append('(')
                        .Append(Number(socket.X)).Append(", ").Append(Number(socket.Y)).Append("))")
                        .Append(j + 1 < frame.Sockets.Length ? "," : string.Empty);
                }

                source.AppendLine().Append(member).AppendLine("};").AppendLine();
            }

            source.Append(member).Append("/// <summary><c>").Append(frame.Name).Append("</c>: ")
                .Append(Number(frame.Width)).Append('x').Append(Number(frame.Height))
                .Append(" at (").Append(Number(frame.X)).Append(", ").Append(Number(frame.Y)).Append(')');
            AppendSocketNames(source, frame);
            source.AppendLine(".</summary>");
            source.Append(member).Append("public static ").Append(SpriteType).Append(' ')
                .Append(Identifier(frame.Name)).Append(" => new ").Append(SpriteType).AppendLine("(");
            source.Append(member).Append("    ").Append(texture).AppendLine(",");
            source.Append(member).Append("    new ").Append(RegionType).Append('(')
                .Append(Number(frame.X)).Append(", ").Append(Number(frame.Y)).Append(", ")
                .Append(Number(frame.Width)).Append(", ").Append(Number(frame.Height)).AppendLine("),");
            source.Append(member).Append("    new ").Append(VectorType).Append('(')
                .Append(Number(frame.PivotX)).Append(", ").Append(Number(frame.PivotY)).Append(')');

            if (frame.Sockets.Length > 0)
            {
                source.AppendLine(",").Append(member).Append("    ").Append(table);
            }

            source.AppendLine(");");
        }

        source.Append(frames).AppendLine("}");

        // No empty class on a sheet declaring none: a consumer naming Sockets is then a compile
        // error rather than a member that never resolves, as with Clips below.
        if (document.Sockets.Length > 0)
        {
            source.AppendLine();
            source.Append(frames).AppendLine("/// <summary>Every socket this sheet's frames set, by name.</summary>");
            source.Append(frames).Append("public static class ").AppendLine(SocketsClass);
            source.Append(frames).AppendLine("{");

            for (int i = 0; i < document.Sockets.Length; i++)
            {
                string socket = document.Sockets[i];
                if (i > 0)
                {
                    source.AppendLine();
                }

                source.Append(member).Append("/// <summary><c>").Append(socket).AppendLine("</c>.</summary>");
                source.Append(member).Append("public const string ").Append(Identifier(socket))
                    .Append(" = ").Append(Literal(socket)).AppendLine(";");
            }

            source.Append(frames).AppendLine("}");
        }

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

    private static void AppendSocketNames(StringBuilder source, SheetFrame frame)
    {
        if (frame.Sockets.Length == 0)
        {
            return;
        }

        source.Append(frame.Sockets.Length == 1 ? "; socket " : "; sockets ");
        for (int i = 0; i < frame.Sockets.Length; i++)
        {
            if (i > 0)
            {
                source.Append(", ");
            }

            source.Append("<c>").Append(frame.Sockets[i].Name).Append("</c>");
        }
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
