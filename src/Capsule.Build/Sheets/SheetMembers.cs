using System.Text;
using Capsule.Assets;
using Capsule.Build.Registry;

namespace Capsule.Build.Sheets;

/// <summary>
/// Writes a sheet's member: a class of typed members, each frame a sprite over one shared socket
/// table, each clip one shared clip and each socket a name constant.
/// </summary>
internal static class SheetMembers
{
    /// <summary>The generated class a sheet's frames are declared on.</summary>
    internal const string FramesClass = "Frames";

    /// <summary>The generated class a sheet's clips are declared on.</summary>
    internal const string ClipsClass = "Clips";

    /// <summary>The generated class a sheet's socket names are declared on.</summary>
    internal const string SocketsClass = "Sockets";

    internal static void Write(StringBuilder code, string indent, string identifier, Source sheet, Sheet document)
    {
        string texture = $"new {GeneratedTypes.TextureHandle}({Literal.Of(document.TextureKey)}, {Literal.Of(document.TextureExtension)})";
        string frames = indent + "    ";
        string member = frames + "    ";

        code.Append(indent).Append("/// <summary>The ")
            .Append(document.Clips.Length == 0 ? "frames" : "frames and clips").Append(" cut from <c>")
            .Append(document.TextureKey).Append(document.TextureExtension).AppendLine("</c>.</summary>");
        code.Append(indent).Append("public static class ").AppendLine(identifier);
        code.Append(indent).AppendLine("{");

        code.Append(frames).AppendLine("/// <summary>Every frame this sheet cuts, as sprites.</summary>");
        code.Append(frames).Append("public static class ").AppendLine(FramesClass);
        code.Append(frames).AppendLine("{");

        for (int i = 0; i < document.Frames.Length; i++)
        {
            SheetFrame frame = document.Frames[i];
            if (i > 0)
            {
                code.AppendLine();
            }

            // One table per frame, read by every sprite the property hands out, so two reads are
            // equal and neither allocates. No frame identifier carries an underscore, so the field
            // name collides with no frame.
            string table = Identifier(frame.Name) + "_Sockets";
            if (frame.Sockets.Length > 0)
            {
                code.Append(member).Append("private static readonly ").Append(GeneratedTypes.SpriteSocket).Append("[] ")
                    .Append(table).AppendLine(" =");
                code.Append(member).AppendLine("{").AppendJoin("," + Environment.NewLine, frame.Sockets.Select(socket =>
                    $"{member}    new {GeneratedTypes.SpriteSocket}({Literal.Of(socket.Name)}, new {GeneratedTypes.Vector2}({Literal.Of(socket.X)}, {Literal.Of(socket.Y)}))"));
                code.AppendLine().Append(member).AppendLine("};").AppendLine();
            }

            code.Append(member).Append("/// <summary><c>").Append(frame.Name).Append("</c>: ")
                .Append(Literal.Of(frame.Width)).Append('x').Append(Literal.Of(frame.Height))
                .Append(" at (").Append(Literal.Of(frame.X)).Append(", ").Append(Literal.Of(frame.Y)).Append(')');
            if (frame.Sockets.Length > 0)
            {
                code.Append(frame.Sockets.Length == 1 ? ", socket " : ", sockets ")
                    .AppendJoin(", ", frame.Sockets.Select(static socket => $"<c>{socket.Name}</c>"));
            }

            code.AppendLine(".</summary>");
            code.Append(member).Append("public static ").Append(GeneratedTypes.Sprite).Append(' ')
                .Append(Identifier(frame.Name)).Append(" => new ").Append(GeneratedTypes.Sprite).AppendLine("(");
            code.Append(member).Append("    ").Append(texture).AppendLine(",");
            code.Append(member).Append("    new ").Append(GeneratedTypes.TextureRegion).Append('(')
                .Append(Literal.Of(frame.X)).Append(", ").Append(Literal.Of(frame.Y)).Append(", ")
                .Append(Literal.Of(frame.Width)).Append(", ").Append(Literal.Of(frame.Height)).AppendLine("),");
            code.Append(member).Append("    new ").Append(GeneratedTypes.Vector2).Append('(')
                .Append(Literal.Of(frame.PivotX)).Append(", ").Append(Literal.Of(frame.PivotY)).Append(')');

            if (frame.Sockets.Length > 0)
            {
                code.AppendLine(",").Append(member).Append("    ").Append(table);
            }

            code.AppendLine(");");
        }

        code.Append(frames).AppendLine("}");

        // A sheet declaring no socket gets no empty class, so naming Sockets is a compile error
        // instead of a member that never resolves. Clips below work the same way.
        if (document.Sockets.Length > 0)
        {
            code.AppendLine();
            code.Append(frames).AppendLine("/// <summary>Every socket this sheet's frames set, by name.</summary>");
            code.Append(frames).Append("public static class ").AppendLine(SocketsClass);
            code.Append(frames).AppendLine("{");

            for (int i = 0; i < document.Sockets.Length; i++)
            {
                string socket = document.Sockets[i];
                if (i > 0)
                {
                    code.AppendLine();
                }

                code.Append(member).Append("/// <summary><c>").Append(socket).AppendLine("</c>.</summary>");
                code.Append(member).Append("public const string ").Append(Identifier(socket))
                    .Append(" = ").Append(Literal.Of(socket)).AppendLine(";");
            }

            code.Append(frames).AppendLine("}");
        }

        if (document.Clips.Length == 0)
        {
            code.Append(indent).AppendLine("}");

            return;
        }

        code.AppendLine();
        code.Append(frames).AppendLine("/// <summary>Every clip this sheet plays over those frames.</summary>");
        code.Append(frames).Append("public static class ").AppendLine(ClipsClass);
        code.Append(frames).AppendLine("{");

        for (int i = 0; i < document.Clips.Length; i++)
        {
            SheetClip clip = document.Clips[i];
            if (i > 0)
            {
                code.AppendLine();
            }

            code.Append(member).Append("/// <summary><c>").Append(clip.Name).Append("</c>: ")
                .Append(Literal.Of(clip.Frames.Length)).Append(clip.Frames.Length == 1 ? " frame, " : " frames, ")
                .Append(Literal.Of(clip.Frames.Sum(static frame => frame.Ticks))).Append(" ticks, ")
                .Append(clip.Loop ? "looping" : "played once").AppendLine(".</summary>");

            // A property with a field initializer, not an expression body. A clip is immutable, so
            // every entity playing it reads the same instance.
            code.Append(member).Append("public static ").Append(GeneratedTypes.SpriteClip).Append(' ')
                .Append(Identifier(clip.Name)).Append(" { get; } = new ").Append(GeneratedTypes.SpriteClip).AppendLine("(");
            code.Append(member).Append("    new ").Append(GeneratedTypes.Sprite).Append("[] { ")
                .AppendJoin(", ", clip.Frames.Select(static frame => FramesClass + "." + Identifier(frame.Frame))).AppendLine(" },");
            code.Append(member).Append("    new int[] { ")
                .AppendJoin(", ", clip.Frames.Select(static frame => Literal.Of(frame.Ticks))).AppendLine(" },");
            code.Append(member).Append("    ").Append(clip.Loop ? "true" : "false").AppendLine(");");
        }

        code.Append(frames).AppendLine("}");
        code.Append(indent).AppendLine("}");
    }

    // The document has already been validated. A name that is not an identifier cannot reach here.
    private static string Identifier(string name) => AssetPaths.ToIdentifier(name)!;
}
