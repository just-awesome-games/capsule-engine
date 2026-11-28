using System.Text;
using Capsule.Assets;
using Capsule.Build.Registry;

namespace Capsule.Build.Sheets;

/// <summary>
/// Writes a sheet's member: a class of typed members, each frame a sprite and each clip a clip of one
/// shared table, and each socket, box and event a name constant.
/// </summary>
/// <remarks>Regions, pivots, marks and clips are constant data loops build into the tables once, and the code does not grow per frame.</remarks>
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

    // The sheet's own members carry an underscore, which no frame, clip or name identifier does.
    private const string Frames = "Frames_Cut";
    private const string All = "Frames_All";
    private const string Clips = "Clips_Cut";

    internal static void Write(StringBuilder code, string indent, string identifier, Source sheet, Sheet document)
    {
        string frames = GeneratedClass.Open(
            code,
            indent,
            $"The {(document.Clips.Length == 0 ? "frames" : "frames and clips")} cut from <c>{document.TextureKey}{document.TextureExtension}</c>.",
            identifier);
        WriteCut(code, frames, document, document.Frames);

        // A variant of this sheet maps its frames in this order.
        code.AppendLine();
        code.Append(frames).Append("internal static global::System.ReadOnlySpan<").Append(GeneratedTypes.Sprite).Append("> ")
            .Append(All).Append(" => ").Append(Frames).AppendLine(";");

        code.AppendLine();
        WriteFrames(code, frames, document.Frames);

        // A sheet declaring no socket gets no empty class. Naming Sockets is then a compile error
        // instead of a member that never resolves. Boxes, events and clips work the same way.
        WriteNames(code, frames, SocketsClass, "Every socket this sheet's frames set, by name.", document.Sockets);
        WriteNames(code, frames, BoxesClass, "Every box this sheet's frames set, by name.", document.Boxes);
        WriteNames(code, frames, EventsClass, "Every event this sheet's clip entries raise, by name.", document.Events);

        if (document.Clips.Length == 0)
        {
            GeneratedClass.Close(code, indent);

            return;
        }

        Dictionary<string, int> index = Indexes(document.Frames.Select(static frame => frame.Name));
        Dictionary<string, int> named = Indexes(document.Events);
        SheetClipFrame[] entries = [.. document.Clips.SelectMany(static clip => clip.Frames)];
        int end = 0;

        code.AppendLine();
        string member = GeneratedClass.Open(code, frames, "Every clip this sheet plays over those frames.", ClipsClass);

        // Every clip, built once. Each member hands back one instance, which every entity playing it shares.
        code.Append(member).Append("private static readonly ").Append(GeneratedTypes.SpriteClip).Append("[] ").Append(Clips).Append(" = ")
            .Append(GeneratedTypes.SheetTables).Append(".Clips(").Append(Frames).Append(", [")
            .AppendJoin(", ", document.Clips.Select(clip => Literal.Of(end += clip.Frames.Length))).Append("], [")
            .AppendJoin(", ", entries.Select(entry => Literal.Of(index[entry.Frame]))).Append("], [")
            .AppendJoin(", ", entries.Select(static entry => Literal.Of(entry.Ticks))).Append("], [")
            .AppendJoin(", ", document.Clips.Select(static clip => clip.Loop ? "true" : "false")).Append("], [")
            .AppendJoin(", ", document.Events.Select(Literal.Of)).Append("], [");
        if (entries.Any(static entry => entry.Events.Length > 0))
        {
            code.AppendJoin(", ", entries.SelectMany(entry => entry.Events.Select(name => named[name]).Prepend(entry.Events.Length)).Select(Literal.Of));
        }

        code.AppendLine("]);");

        for (int i = 0; i < document.Clips.Length; i++)
        {
            SheetClip clip = document.Clips[i];
            code.AppendLine();
            code.Append(member).Append("/// <summary><c>").Append(clip.Name).Append("</c>: ")
                .Append(Literal.Of(clip.Frames.Length)).Append(clip.Frames.Length == 1 ? " frame, " : " frames, ")
                .Append(Literal.Of(clip.Frames.Sum(static frame => frame.Ticks))).Append(" ticks, ")
                .Append(clip.Loop ? "looping" : "played once").AppendLine(".</summary>");
            code.Append(member).Append("public static ").Append(GeneratedTypes.SpriteClip).Append(' ')
                .Append(AssetPaths.Identifier(clip.Name)).Append(" => ").Append(Clips).Append('[').Append(Literal.Of(i)).AppendLine("];");
        }

        GeneratedClass.Close(code, frames);
        GeneratedClass.Close(code, indent);
    }

    /// <summary>
    /// Writes a variant sheet's member: a class holding the frames it draws, as sprites, and the
    /// <c>Variant</c> mapping each of its base's frames to the frame of the same name.
    /// </summary>
    internal static void WriteVariant(StringBuilder code, string indent, string identifier, Source sheet, SheetVariant model)
    {
        Sheet document = model.Variant;
        SheetFrame[] drawn = [.. document.Frames.Where(static frame => !frame.IsEmpty)];
        Dictionary<string, int> index = Indexes(drawn.Select(static frame => frame.Name));
        string frames = GeneratedClass.Open(
            code,
            indent,
            $"The frames of <c>{model.BaseKey}{AssetType.Sheets.Extensions[0]}</c> drawn from <c>{document.TextureKey}{document.TextureExtension}</c>.",
            identifier);
        string member = frames + "    ";

        // A variant whose every frame draws nothing gets no empty Frames class. Its table precedes
        // Variant, whose initializer reads it.
        if (drawn.Length > 0)
        {
            WriteCut(code, frames, document, drawn);
            code.AppendLine();
            WriteFrames(code, frames, drawn);
            code.AppendLine();
        }

        // A property with a field initializer: every renderer holding the variant reads one instance.
        code.Append(frames).AppendLine("/// <summary>Each base frame mapped to this sheet's frame of the same name, or to nothing where that frame draws nothing.</summary>");
        code.Append(frames).Append("public static ").Append(GeneratedTypes.SpriteVariant).Append(" Variant { get; } = new ")
            .Append(GeneratedTypes.SpriteVariant).AppendLine("(");
        code.Append(member).Append(BaseClass(model.BaseKey)).Append('.').Append(All).AppendLine(",");
        code.Append(member).Append(GeneratedTypes.SheetTables).Append(".Drawn(").Append(drawn.Length > 0 ? Frames : "default").Append(", [")
            .AppendJoin(", ", model.Base.Frames.Select(frame => Literal.Of(index.TryGetValue(frame.Name, out int at) ? at : -1))).AppendLine("]));");
        GeneratedClass.Close(code, indent);
    }

    // Each name's position in names.
    private static Dictionary<string, int> Indexes(IEnumerable<string> names)
    {
        Dictionary<string, int> index = new(StringComparer.Ordinal);
        foreach (string name in names)
        {
            index[name] = index.Count;
        }

        return index;
    }

    // The generated class of the sheet keyed `key`, fully qualified, as CapsuleAssetsFile declares it.
    private static string BaseClass(string key)
    {
        int slash = key.LastIndexOf('/');
        string folders = slash < 0 ? string.Empty : string.Concat(key[..slash].Split('/').Select(static folder => "." + AssetPaths.Identifier(folder)));

        return GeneratedTypes.CapsuleAssets + folders + "." + AssetPaths.Identifier(key[(slash + 1)..]) + AssetType.Sheets.Suffix;
    }

    // The sheet class's table of every frame in list, in order, with each marked frame's marks on its
    // own sprite. A frame's member reads its own entry. Two reads are then equal, and neither allocates.
    private static void WriteCut(StringBuilder code, string indent, Sheet document, SheetFrame[] list)
    {
        string cut = GeneratedTypes.SheetTables + ".Cut(new " + GeneratedTypes.TextureHandle + "(" + Literal.Of(document.TextureKey) + ", "
            + Literal.Of(document.TextureExtension) + "), ["
            + string.Join(", ", list.SelectMany(static frame => new[] { frame.X, frame.Y, frame.Width, frame.Height }).Select(Literal.Of)) + "], ["
            + string.Join(", ", list.SelectMany(static frame => new[] { frame.PivotX, frame.PivotY }).Select(Literal.Of)) + "], ["
            + (list.Any(static frame => frame.FlipX || frame.FlipY)
                ? string.Join(", ", list.Select(static frame => Literal.Of((frame.FlipX ? 1 : 0) | (frame.FlipY ? 2 : 0))))
                : string.Empty) + "])";

        code.Append(indent).Append("private static readonly ").Append(GeneratedTypes.Sprite).Append("[] ").Append(Frames).Append(" = ");
        if (!list.Any(static frame => frame.Sockets.Length > 0 || frame.Boxes.Length > 0))
        {
            code.Append(cut).AppendLine(";");

            return;
        }

        Dictionary<string, int> sockets = Indexes(document.Sockets);
        Dictionary<string, int> boxes = Indexes(document.Boxes);
        List<int> marks = [];
        List<float> points = [];
        for (int i = 0; i < list.Length; i++)
        {
            SheetFrame frame = list[i];
            if (frame.Sockets.Length == 0 && frame.Boxes.Length == 0)
            {
                continue;
            }

            marks.AddRange([i, frame.Sockets.Length, frame.Boxes.Length]);
            marks.AddRange(frame.Sockets.Select(socket => sockets[socket.Name]));
            marks.AddRange(frame.Boxes.Select(box => boxes[box.Name]));
            points.AddRange(frame.Sockets.SelectMany(static socket => new[] { socket.X, socket.Y }));
            points.AddRange(frame.Boxes.SelectMany(static box => new[] { box.X, box.Y, box.Width, box.Height }));
        }

        code.Append(GeneratedTypes.SheetTables).Append(".Mark(").Append(cut).Append(", [")
            .AppendJoin(", ", document.Sockets.Select(Literal.Of)).Append("], [")
            .AppendJoin(", ", document.Boxes.Select(Literal.Of)).Append("], [")
            .AppendJoin(", ", marks.Select(Literal.Of)).Append("], [")
            .AppendJoin(", ", points.Select(Literal.Of)).AppendLine("]);");
    }

    private static void WriteFrames(StringBuilder code, string indent, SheetFrame[] list)
    {
        string member = GeneratedClass.Open(code, indent, "Every frame this sheet cuts, as sprites.", FramesClass);

        for (int i = 0; i < list.Length; i++)
        {
            SheetFrame frame = list[i];
            if (i > 0)
            {
                code.AppendLine();
            }

            code.Append(member).Append("/// <summary><c>").Append(frame.Name).Append("</c>: ")
                .Append(Literal.Of(frame.Width)).Append('x').Append(Literal.Of(frame.Height))
                .Append(" at (").Append(Literal.Of(frame.X)).Append(", ").Append(Literal.Of(frame.Y)).Append(')')
                .Append(frame.FlipX ? ", flipped x" : string.Empty).Append(frame.FlipY ? ", flipped y" : string.Empty);
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
                .Append(AssetPaths.Identifier(frame.Name)).Append(" => ").Append(Frames).Append('[').Append(Literal.Of(i)).AppendLine("];");
        }

        GeneratedClass.Close(code, indent);
    }

    private static void WriteNames(StringBuilder code, string indent, string className, string summary, string[] names)
    {
        if (names.Length == 0)
        {
            return;
        }

        code.AppendLine();
        string member = GeneratedClass.Open(code, indent, summary, className);

        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0)
            {
                code.AppendLine();
            }

            code.Append(member).Append("/// <summary><c>").Append(names[i]).AppendLine("</c>.</summary>");
            code.Append(member).Append("public const string ").Append(AssetPaths.Identifier(names[i]))
                .Append(" = ").Append(Literal.Of(names[i])).AppendLine(";");
        }

        GeneratedClass.Close(code, indent);
    }
}
