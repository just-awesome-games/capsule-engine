using System.Text;
using Capsule.Build.Registry;
using Capsule.Generators;

namespace Capsule.Build.Sprites;

/// <summary>
/// Every sprite sheet, compiled into a class of typed members: each frame a sprite over one shared
/// socket table, each clip one shared clip, each socket a name constant. A misspelt frame, clip or
/// socket is a compile error, and no sheet ships beside the executable.
/// </summary>
internal static class SpriteStep
{
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

    internal static void Build(BuildPass pass)
    {
        Dictionary<string, string> textures = pass.Of(AssetType.Textures)
            .ToDictionary(static texture => texture.Key, static texture => texture.Extension, StringComparer.Ordinal);

        foreach ((Source sheet, SpriteSheet document) in pass.Each(
            pass.Of(AssetType.Sprites),
            source =>
            {
                SpriteSheet sheet = SpriteSheetFile.Read(source.Path);

                // Resolved by key against what the build ships, and carrying the shipped extension,
                // however the sheet spelled it.
                return textures.TryGetValue(sheet.TextureKey, out string? extension)
                    ? sheet with { TextureExtension = extension }
                    : throw new FormatException(
                        $"cuts from texture \"{sheet.TextureKey}{sheet.TextureExtension}\", which this game does not ship. Author it at Assets/{sheet.TextureKey}{sheet.TextureExtension}.");
            }))
        {
            pass.Declare(sheet, (source, indent, identifier) => AppendSheet(source, indent, identifier, document));
        }
    }

    private static void AppendSheet(StringBuilder source, string indent, string identifier, SpriteSheet document)
    {
        string texture = $"new {TextureType}({Literal.Of(document.TextureKey)}, {Literal.Of(document.TextureExtension)})";
        string frames = indent + "    ";
        string member = frames + "    ";

        source.Append(indent).Append("/// <summary>The ")
            .Append(document.Clips.Length == 0 ? "frames" : "frames and clips").Append(" cut from <c>")
            .Append(document.TextureKey).Append(document.TextureExtension).AppendLine("</c>.</summary>");
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

            // One table per frame, read by every sprite the property hands out, so two reads are
            // equal and neither allocates. No frame identifier carries an underscore, so the field
            // name collides with no frame.
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
                        .Append(Literal.Of(socket.Name)).Append(", new ").Append(VectorType).Append('(')
                        .Append(Literal.Of(socket.X)).Append(", ").Append(Literal.Of(socket.Y)).Append("))")
                        .Append(j + 1 < frame.Sockets.Length ? "," : string.Empty);
                }

                source.AppendLine().Append(member).AppendLine("};").AppendLine();
            }

            source.Append(member).Append("/// <summary><c>").Append(frame.Name).Append("</c>: ")
                .Append(Literal.Of(frame.Width)).Append('x').Append(Literal.Of(frame.Height))
                .Append(" at (").Append(Literal.Of(frame.X)).Append(", ").Append(Literal.Of(frame.Y)).Append(')');
            AppendSocketNames(source, frame);
            source.AppendLine(".</summary>");
            source.Append(member).Append("public static ").Append(SpriteType).Append(' ')
                .Append(Identifier(frame.Name)).Append(" => new ").Append(SpriteType).AppendLine("(");
            source.Append(member).Append("    ").Append(texture).AppendLine(",");
            source.Append(member).Append("    new ").Append(RegionType).Append('(')
                .Append(Literal.Of(frame.X)).Append(", ").Append(Literal.Of(frame.Y)).Append(", ")
                .Append(Literal.Of(frame.Width)).Append(", ").Append(Literal.Of(frame.Height)).AppendLine("),");
            source.Append(member).Append("    new ").Append(VectorType).Append('(')
                .Append(Literal.Of(frame.PivotX)).Append(", ").Append(Literal.Of(frame.PivotY)).Append(')');

            if (frame.Sockets.Length > 0)
            {
                source.AppendLine(",").Append(member).Append("    ").Append(table);
            }

            source.AppendLine(");");
        }

        source.Append(frames).AppendLine("}");

        // A sheet declaring no socket gets no empty class, so naming Sockets is a compile error
        // instead of a member that never resolves. Clips below work the same way.
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
                    .Append(" = ").Append(Literal.Of(socket)).AppendLine(";");
            }

            source.Append(frames).AppendLine("}");
        }

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
                .Append(Literal.Of(clip.Frames.Length)).Append(clip.Frames.Length == 1 ? " frame, " : " frames, ")
                .Append(Literal.Of(TotalTicks(clip))).Append(" ticks, ")
                .Append(clip.Loop ? "looping" : "played once").AppendLine(".</summary>");

            // A property with a field initializer, not an expression body. A clip is immutable, so
            // every entity playing it reads the same instance.
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

                source.Append(Literal.Of(clip.Frames[j].Ticks));
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

        source.Append(frame.Sockets.Length == 1 ? ", socket " : ", sockets ");
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

    // The document has already been validated. A name that is not an identifier cannot reach here.
    private static string Identifier(string name) => TypeNaming.ToIdentifier(name)!;
}
