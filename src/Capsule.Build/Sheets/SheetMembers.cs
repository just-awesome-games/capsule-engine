using System.Text;
using Capsule.Assets;
using Capsule.Build.Registry;

namespace Capsule.Build.Sheets;

/// <summary>
/// Writes a sheet's member: a class of typed members, each frame a sprite over one shared marks
/// table, each clip one shared clip and each socket, box and event a name constant.
/// </summary>
internal static class SheetMembers
{
    /// <summary>The generated class a sheet's frames are declared on.</summary>
    internal const string FramesClass = "Frames";

    /// <summary>The generated class a sheet's clips are declared on.</summary>
    internal const string ClipsClass = "Clips";

    /// <summary>The generated class a sheet's socket names are declared on.</summary>
    internal const string SocketsClass = "Sockets";

    /// <summary>The generated class a sheet's box names are declared on.</summary>
    internal const string BoxesClass = "Boxes";

    /// <summary>The generated class a sheet's event names are declared on.</summary>
    internal const string EventsClass = "Events";

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
            string table = Identifier(frame.Name) + "_Marks";
            bool marked = frame.Sockets.Length > 0 || frame.Boxes.Length > 0;
            if (marked)
            {
                code.Append(member).Append("private static readonly ").Append(GeneratedTypes.SpriteMarks).Append(' ')
                    .Append(table).Append(" = new ").Append(GeneratedTypes.SpriteMarks).Append('(');
                string separator = "";
                if (frame.Sockets.Length > 0)
                {
                    code.AppendLine().Append(member).Append("    sockets: new ").Append(GeneratedTypes.SpriteSocket).AppendLine("[]");
                    code.Append(member).AppendLine("    {").AppendJoin("," + Environment.NewLine, frame.Sockets.Select(socket =>
                        $"{member}        new {GeneratedTypes.SpriteSocket}({Literal.Of(socket.Name)}, new {GeneratedTypes.Vector2}({Literal.Of(socket.X)}, {Literal.Of(socket.Y)}))"));
                    code.AppendLine().Append(member).Append("    }");
                    separator = ",";
                }

                if (frame.Boxes.Length > 0)
                {
                    code.AppendLine(separator).Append(member).Append("    boxes: new ").Append(GeneratedTypes.SpriteBox).AppendLine("[]");
                    code.Append(member).AppendLine("    {").AppendJoin("," + Environment.NewLine, frame.Boxes.Select(box =>
                        $"{member}        new {GeneratedTypes.SpriteBox}({Literal.Of(box.Name)}, new {GeneratedTypes.Rect}(new {GeneratedTypes.Vector2}({Literal.Of(box.X)}, {Literal.Of(box.Y)}), new {GeneratedTypes.Vector2}({Literal.Of(box.Width)}, {Literal.Of(box.Height)})))"));
                    code.AppendLine().Append(member).Append("    }");
                }

                code.AppendLine(");").AppendLine();
            }

            code.Append(member).Append("/// <summary><c>").Append(frame.Name).Append("</c>: ")
                .Append(Literal.Of(frame.Width)).Append('x').Append(Literal.Of(frame.Height))
                .Append(" at (").Append(Literal.Of(frame.X)).Append(", ").Append(Literal.Of(frame.Y)).Append(')');
            if (frame.Sockets.Length > 0)
            {
                code.Append(frame.Sockets.Length == 1 ? ", socket " : ", sockets ")
                    .AppendJoin(", ", frame.Sockets.Select(static socket => $"<c>{socket.Name}</c>"));
            }

            if (frame.Boxes.Length > 0)
            {
                code.Append(frame.Boxes.Length == 1 ? ", box " : ", boxes ")
                    .AppendJoin(", ", frame.Boxes.Select(static box => $"<c>{box.Name}</c>"));
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

            if (marked)
            {
                code.AppendLine(",").Append(member).Append("    ").Append(table);
            }

            code.AppendLine(");");
        }

        code.Append(frames).AppendLine("}");

        // A sheet declaring no socket gets no empty class, so naming Sockets is a compile error
        // instead of a member that never resolves. Boxes, events and clips work the same way.
        WriteNames(code, frames, SocketsClass, "Every socket this sheet's frames set, by name.", document.Sockets);
        WriteNames(code, frames, BoxesClass, "Every box this sheet's frames set, by name.", document.Boxes);
        WriteNames(code, frames, EventsClass, "Every event this sheet's clip entries raise, by name.", document.Events);

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
            code.Append(member).Append("    ").Append(clip.Loop ? "true" : "false");
            if (clip.Frames.Any(static frame => frame.Events.Length > 0))
            {
                code.AppendLine(",").Append(member).Append("    new string[][] { ")
                    .AppendJoin(", ", clip.Frames.Select(static frame => frame.Events.Length == 0
                        ? "global::System.Array.Empty<string>()"
                        : "new string[] { " + string.Join(", ", frame.Events.Select(Literal.Of)) + " }"))
                    .Append(" }");
            }

            code.AppendLine(");");
        }

        code.Append(frames).AppendLine("}");
        code.Append(indent).AppendLine("}");
    }

    private static void WriteNames(StringBuilder code, string indent, string className, string summary, string[] names)
    {
        if (names.Length == 0)
        {
            return;
        }

        string member = indent + "    ";
        code.AppendLine();
        code.Append(indent).Append("/// <summary>").Append(summary).AppendLine("</summary>");
        code.Append(indent).Append("public static class ").AppendLine(className);
        code.Append(indent).AppendLine("{");

        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0)
            {
                code.AppendLine();
            }

            code.Append(member).Append("/// <summary><c>").Append(names[i]).AppendLine("</c>.</summary>");
            code.Append(member).Append("public const string ").Append(Identifier(names[i]))
                .Append(" = ").Append(Literal.Of(names[i])).AppendLine(";");
        }

        code.Append(indent).AppendLine("}");
    }

    // The document has already been validated. A name that is not an identifier cannot reach here.
    private static string Identifier(string name) => AssetPaths.ToIdentifier(name)!;
}
