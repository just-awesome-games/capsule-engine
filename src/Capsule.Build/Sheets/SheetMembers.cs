using System.Text;
using Capsule.Assets;
using Capsule.Build.Registry;

namespace Capsule.Build.Sheets;

/// <summary>
/// Writes a sheet's member: a class of typed members, each frame a sprite and each clip a clip of one
/// shared table, and each socket, box and event a name constant.
/// </summary>
/// <remarks>
/// A sheet's regions, pivots, marks and clips are constant data that loops build into its tables once.
/// Building each frame and clip in code of its own compiled to code growing with every frame a game draws.
/// </remarks>
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

    /// <summary>The class beside <c>CapsuleAssets</c> that builds every sheet's tables from its data.</summary>
    internal const string BuilderClass = "CapsuleSheetTables";

    // The sheet's own members carry an underscore, which no frame, clip or name identifier does.
    private const string Frames = "Frames_Cut";
    private const string All = "Frames_All";
    private const string Clips = "Clips_Cut";
    private const string Builder = "global::Capsule.Generated." + BuilderClass;

    /// <summary>The declaration of <see cref="BuilderClass"/>, which every sheet's tables are built by.</summary>
    internal const string BuilderDeclaration = $$"""
            /// <summary>Builds each sheet's frames and clips from the constant data the build wrote. Generated code.</summary>
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            internal static class {{BuilderClass}}
            {
                /// <summary>
                /// Each frame's sprite, from its region as x, y, width and height, and its pivot as x and y. Where any frame
                /// flips, flips holds each frame's, 1 for x and 2 for y.
                /// </summary>
                internal static {{GeneratedTypes.Sprite}}[] Cut({{GeneratedTypes.TextureHandle}} texture, global::System.ReadOnlySpan<int> regions, global::System.ReadOnlySpan<float> pivots, global::System.ReadOnlySpan<int> flips)
                {
                    {{GeneratedTypes.Sprite}}[] frames = new {{GeneratedTypes.Sprite}}[pivots.Length / 2];
                    for (int i = 0; i < frames.Length; i++)
                    {
                        frames[i] = new {{GeneratedTypes.Sprite}}(
                            texture,
                            new {{GeneratedTypes.TextureRegion}}(regions[i * 4], regions[(i * 4) + 1], regions[(i * 4) + 2], regions[(i * 4) + 3]),
                            new {{GeneratedTypes.Vector2}}(pivots[i * 2], pivots[(i * 2) + 1]),
                            FlipX: !flips.IsEmpty && (flips[i] & 1) != 0,
                            FlipY: !flips.IsEmpty && (flips[i] & 2) != 0);
                    }

                    return frames;
                }

                /// <summary>
                /// Sets each marked frame's marks. Per marked frame, marks holds its index, its socket and box counts
                /// and each one's name index, and points each socket's x and y, then each box's x, y, width and height.
                /// </summary>
                internal static {{GeneratedTypes.Sprite}}[] Mark({{GeneratedTypes.Sprite}}[] frames, global::System.ReadOnlySpan<string> sockets, global::System.ReadOnlySpan<string> boxes, global::System.ReadOnlySpan<int> marks, global::System.ReadOnlySpan<float> points)
                {
                    for (int at = 0, point = 0; at < marks.Length;)
                    {
                        int frame = marks[at];
                        {{GeneratedTypes.SpriteSocket}}[] set = new {{GeneratedTypes.SpriteSocket}}[marks[at + 1]];
                        {{GeneratedTypes.SpriteBox}}[] boxed = new {{GeneratedTypes.SpriteBox}}[marks[at + 2]];
                        at += 3;
                        for (int i = 0; i < set.Length; i++, point += 2)
                        {
                            set[i] = new {{GeneratedTypes.SpriteSocket}}(sockets[marks[at++]], new {{GeneratedTypes.Vector2}}(points[point], points[point + 1]));
                        }

                        for (int i = 0; i < boxed.Length; i++, point += 4)
                        {
                            boxed[i] = new {{GeneratedTypes.SpriteBox}}(boxes[marks[at++]], new {{GeneratedTypes.Rect}}(points[point], points[point + 1], points[point + 2], points[point + 3]));
                        }

                        frames[frame] = frames[frame] with { Marks = new {{GeneratedTypes.SpriteMarks}}(set, boxed) };
                    }

                    return frames;
                }

                /// <summary>
                /// Each clip, over the entries up to its end: each entry's frame index and ticks, and, where events
                /// holds any, its event count and each one's name index.
                /// </summary>
                internal static {{GeneratedTypes.SpriteClip}}[] Clips(global::System.ReadOnlySpan<{{GeneratedTypes.Sprite}}> frames, global::System.ReadOnlySpan<int> ends, global::System.ReadOnlySpan<int> picks, global::System.ReadOnlySpan<int> ticks, global::System.ReadOnlySpan<bool> loops, global::System.ReadOnlySpan<string> names, global::System.ReadOnlySpan<int> events)
                {
                    {{GeneratedTypes.SpriteClip}}[] clips = new {{GeneratedTypes.SpriteClip}}[ends.Length];
                    for (int i = 0, start = 0, at = 0; i < clips.Length; start = ends[i], i++)
                    {
                        {{GeneratedTypes.Sprite}}[] played = new {{GeneratedTypes.Sprite}}[ends[i] - start];
                        string[][] raised = events.IsEmpty ? [] : new string[played.Length][];
                        for (int entry = 0; entry < played.Length; entry++)
                        {
                            played[entry] = frames[picks[start + entry]];
                            if (!events.IsEmpty)
                            {
                                raised[entry] = new string[events[at++]];
                                for (int name = 0; name < raised[entry].Length; name++)
                                {
                                    raised[entry][name] = names[events[at++]];
                                }
                            }
                        }

                        clips[i] = new {{GeneratedTypes.SpriteClip}}(played, ticks[start..ends[i]], loops[i], raised);
                    }

                    return clips;
                }

                /// <summary>The frames at <paramref name="picks"/>, in order, a negative pick drawing nothing.</summary>
                internal static {{GeneratedTypes.Sprite}}?[] Drawn(global::System.ReadOnlySpan<{{GeneratedTypes.Sprite}}> frames, global::System.ReadOnlySpan<int> picks)
                {
                    {{GeneratedTypes.Sprite}}?[] drawn = new {{GeneratedTypes.Sprite}}?[picks.Length];
                    for (int i = 0; i < picks.Length; i++)
                    {
                        drawn[i] = picks[i] < 0 ? null : frames[picks[i]];
                    }

                    return drawn;
                }
            }

        """;

    internal static void Write(StringBuilder code, string indent, string identifier, Source sheet, Sheet document)
    {
        string frames = indent + "    ";
        string member = frames + "    ";

        code.Append(indent).Append("/// <summary>The ")
            .Append(document.Clips.Length == 0 ? "frames" : "frames and clips").Append(" cut from <c>")
            .Append(document.TextureKey).Append(document.TextureExtension).AppendLine("</c>.</summary>");
        code.Append(indent).Append("public static class ").AppendLine(identifier);
        code.Append(indent).AppendLine("{");

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
            code.Append(indent).AppendLine("}");

            return;
        }

        Dictionary<string, int> index = Indexes(document.Frames.Select(static frame => frame.Name));
        Dictionary<string, int> named = Indexes(document.Events);
        SheetClipFrame[] entries = [.. document.Clips.SelectMany(static clip => clip.Frames)];
        int end = 0;

        code.AppendLine();
        code.Append(frames).AppendLine("/// <summary>Every clip this sheet plays over those frames.</summary>");
        code.Append(frames).Append("public static class ").AppendLine(ClipsClass);
        code.Append(frames).AppendLine("{");

        // Every clip, built once. Each member hands back one instance, which every entity playing it shares.
        code.Append(member).Append("private static readonly ").Append(GeneratedTypes.SpriteClip).Append("[] ").Append(Clips).Append(" = ")
            .Append(Builder).Append(".Clips(").Append(Frames).Append(", [")
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
                .Append(Identifier(clip.Name)).Append(" => ").Append(Clips).Append('[').Append(Literal.Of(i)).AppendLine("];");
        }

        code.Append(frames).AppendLine("}");
        code.Append(indent).AppendLine("}");
    }

    /// <summary>
    /// Writes a variant sheet's member: a class holding the frames it draws, as sprites, and the
    /// <c>Variant</c> mapping each of its base's frames to the frame of the same name.
    /// </summary>
    internal static void WriteVariant(StringBuilder code, string indent, string identifier, Source sheet, SheetVariant model)
    {
        Sheet document = model.Variant;
        string frames = indent + "    ";
        string member = frames + "    ";
        SheetFrame[] drawn = [.. document.Frames.Where(static frame => !frame.IsEmpty)];
        Dictionary<string, int> index = Indexes(drawn.Select(static frame => frame.Name));

        code.Append(indent).Append("/// <summary>The frames of <c>").Append(model.BaseKey).Append(AssetType.Sheets.Extensions[0])
            .Append("</c> drawn from <c>").Append(document.TextureKey).Append(document.TextureExtension).AppendLine("</c>.</summary>");
        code.Append(indent).Append("public static class ").AppendLine(identifier);
        code.Append(indent).AppendLine("{");

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
        code.Append(member).Append(Builder).Append(".Drawn(").Append(drawn.Length > 0 ? Frames : "default").Append(", [")
            .AppendJoin(", ", model.Base.Frames.Select(frame => Literal.Of(index.TryGetValue(frame.Name, out int at) ? at : -1))).AppendLine("]));");
        code.Append(indent).AppendLine("}");
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
        string folders = slash < 0 ? string.Empty : string.Concat(key[..slash].Split('/').Select(static folder => "." + Identifier(folder)));

        return GeneratedTypes.CapsuleAssets + folders + "." + Identifier(key[(slash + 1)..]) + AssetType.Sheets.Suffix;
    }

    // The sheet class's table of every frame in list, in order, with each marked frame's marks on its
    // own sprite. A frame's member reads its own entry. Two reads are then equal, and neither allocates.
    private static void WriteCut(StringBuilder code, string indent, Sheet document, SheetFrame[] list)
    {
        string cut = Builder + ".Cut(new " + GeneratedTypes.TextureHandle + "(" + Literal.Of(document.TextureKey) + ", "
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

        code.Append(Builder).Append(".Mark(").Append(cut).Append(", [")
            .AppendJoin(", ", document.Sockets.Select(Literal.Of)).Append("], [")
            .AppendJoin(", ", document.Boxes.Select(Literal.Of)).Append("], [")
            .AppendJoin(", ", marks.Select(Literal.Of)).Append("], [")
            .AppendJoin(", ", points.Select(Literal.Of)).AppendLine("]);");
    }

    private static void WriteFrames(StringBuilder code, string indent, SheetFrame[] list)
    {
        string member = indent + "    ";
        code.Append(indent).AppendLine("/// <summary>Every frame this sheet cuts, as sprites.</summary>");
        code.Append(indent).Append("public static class ").AppendLine(FramesClass);
        code.Append(indent).AppendLine("{");

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
                .Append(Identifier(frame.Name)).Append(" => ").Append(Frames).Append('[').Append(Literal.Of(i)).AppendLine("];");
        }

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
