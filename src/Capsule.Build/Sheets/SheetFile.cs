using Capsule.Assets;
using Capsule.Build.Configuration;

namespace Capsule.Build.Sheets;

/// <summary>
/// Reads the sheet document format. The sheet is known at build time.
/// </summary>
/// <remarks>
/// The generated registry carries it as literal data, and no document ships or is parsed at
/// run time.
/// </remarks>
internal static class SheetFile
{
    /// <summary>Reads and validates the sheet at <paramref name="path"/>.</summary>
    /// <exception cref="FormatException">The JSON is malformed or the document breaks the format.</exception>
    internal static Sheet Read(string path)
    {
        // A null reads as the member left out. A tool writing its whole schema out then authors the
        // same sheet as one that omits what it has nothing to say about.
        return Validate(StrictJson.Read(path, SheetJsonContext.Default.SheetJson, SheetJson.Members, nullFix: null, SheetJson.Shape));
    }

    private static Sheet Validate(SheetJson json)
    {
        if (json.FormatVersion is not { } format)
        {
            throw new FormatException($"has no formatVersion. This build supports formatVersion {SheetJson.SupportedFormat}. Set \"formatVersion\": {SheetJson.SupportedFormat}.");
        }

        if (format != SheetJson.SupportedFormat)
        {
            throw new FormatException(
                $"declares formatVersion {format}, which is unsupported. This build supports formatVersion {SheetJson.SupportedFormat}. Write the sheet in that version.");
        }

        (string key, string extension) = Texture(json.Texture);
        if (json.VariantOf is { } variantOf)
        {
            return new Sheet(key, extension, [], [], [], VariantFrames(json), [], BaseSheet(variantOf));
        }

        // Sockets, boxes and events each have their own name space. One may share a name with a frame,
        // a clip or a member of another list.
        string[] sockets = Declared(json.Sockets?.ConvertAll(static socket => socket?.Name), SheetMembers.SocketsClass, "sockets");
        string[] boxes = Declared(json.Boxes?.ConvertAll(static box => box?.Name), SheetMembers.BoxesClass, "boxes");
        string[] events = Declared(json.Events?.ConvertAll(static raised => raised?.Name), SheetMembers.EventsClass, "events");
        SheetFrame[] frames = Frames(json.Frames, sockets, boxes);

        return new Sheet(key, extension, sockets, boxes, events, frames, Clips(json.Clips, frames, events), VariantOf: null);
    }

    // The base is named by its path under Assets/ as a texture is, and reached by its key however it is spelled.
    private static string BaseSheet(string path)
    {
        string suffix = AssetType.Sheets.Extensions[0];
        if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) || !AssetPaths.TrySplit(path, out _, out _))
        {
            throw new FormatException(
                $"is a variant of \"{path}\". A base sheet is named by its path under Assets/, extension included, as \"Sprites/avatar.sheet.json\", with forward slashes and no empty, \".\" or \"..\" segment.");
        }

        return Keys.Of(path[..^suffix.Length], $"is a variant of \"{path}\"");
    }

    // A variant draws its base's frames from its own texture. The clips, events and marks are the base's.
    private static SheetFrame[] VariantFrames(SheetJson json)
    {
        string? declared = json.Clips is { Count: > 0 } ? "clips"
            : json.Events is { Count: > 0 } ? "events"
            : json.Sockets is { Count: > 0 } ? "sockets"
            : json.Boxes is { Count: > 0 } ? "boxes"
            : null;
        if (declared is not null)
        {
            throw new FormatException(
                $"declares \"{declared}\" and is a variant of \"{json.VariantOf}\". A variant only draws its base's frames another way, and plays, raises and places what its base declares. Remove \"{declared}\".");
        }

        if (json.Frames is not { Count: >= SheetJson.NonEmpty } declaredFrames)
        {
            throw new FormatException(
                $"has no frames and is a variant of \"{json.VariantOf}\". A variant lists every frame of its base once. Add each base frame to \"frames\".");
        }

        SheetFrame[] frames = new SheetFrame[declaredFrames.Count];
        Names named = new(SheetMembers.FramesClass);

        for (int i = 0; i < declaredFrames.Count; i++)
        {
            if (declaredFrames[i] is not { } frame)
            {
                throw new FormatException($"has frames[{i}] as null. Every frame is an object. Write the frame, or remove the null.");
            }

            string name = named.Read(frame.Name, $"frames[{i}]");

            if (frame.Sockets is { Count: > 0 } || frame.Boxes is { Count: > 0 })
            {
                throw new FormatException(
                    $"has frame \"{name}\" setting a {(frame.Sockets is { Count: > 0 } ? "socket" : "box")}, and is a variant. Sockets and boxes come from the base's frame, and a variant never moves them. Remove them from the variant's frame.");
            }

            frames[i] = frame.X is null && frame.Y is null && frame.Width is null && frame.Height is null && frame.Pivot is null
                ? new SheetFrame(name, 0, 0, 0, 0, 0F, 0F, [], [], IsEmpty: true)
                : Region(frame, name, [], []);
        }

        return frames;
    }
    private static (string Key, string Extension) Texture(string? path)
    {
        if (path is not { Length: >= SheetJson.NonEmpty })
        {
            throw new FormatException("names no texture. A sheet cuts its frames from one texture. Set \"texture\" to its key, as \"player.png\".");
        }

        if (!AssetPaths.TrySplit(path, out string name, out string extension))
        {
            throw new FormatException(
                $"has texture \"{path}\". A texture is named by its key, extension included, as \"player.png\" or \"actors/player.png\", with forward slashes and no empty, \".\" or \"..\" segment.");
        }

        // A texture is reached by its key however the document spelled it. The handle emitted here
        // names the key the build ships the texture under.
        return (Keys.Of(name, $"has texture \"{path}\""), extension);
    }

    private static string[] Declared(List<string?>? declared, string reserved, string list)
    {
        if (declared is not { Count: > 0 })
        {
            return [];
        }

        Names named = new(reserved);
        string[] names = new string[declared.Count];

        for (int i = 0; i < declared.Count; i++)
        {
            names[i] = named.Read(declared[i], $"{list}[{i}]");
        }

        return names;
    }

    private static SheetFrame[] Frames(List<FrameJson>? declared, string[] sockets, string[] boxes)
    {
        if (declared is not { Count: >= SheetJson.NonEmpty })
        {
            throw new FormatException("has no frames. A sheet names at least one region of its texture. Add a frame to \"frames\".");
        }

        SheetFrame[] frames = new SheetFrame[declared.Count];
        Names named = new(SheetMembers.FramesClass);
        bool[] set = new bool[sockets.Length];
        bool[] boxesSet = new bool[boxes.Length];

        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i] is not { } frame)
            {
                throw new FormatException($"has frames[{i}] as null. Every frame is an object. Write the frame, or remove the null.");
            }

            string name = named.Read(frame.Name, $"frames[{i}]");
            frames[i] = Region(
                frame,
                name,
                FrameMarks(frame.Sockets, name, sockets, set, "socket", "sockets", Socket),
                FrameMarks(frame.Boxes, name, boxes, boxesSet, "box", "boxes", Box));
        }

        RequireAllSet(sockets, set, "socket", "a point");
        RequireAllSet(boxes, boxesSet, "box", "a rect");

        return frames;
    }

    private static SheetFrame Region(FrameJson frame, string name, SheetSocket[] sockets, SheetBox[] boxes)
    {
        if (frame.X is not { } x || frame.Y is not { } y || frame.Width is not { } width || frame.Height is not { } height)
        {
            throw new FormatException(
                $"has frame \"{name}\" with no {Missing(frame)}. Every frame carries x, y, width and height in texels. Add the missing one.");
        }

        if (x < SheetJson.LeastOrigin || y < SheetJson.LeastOrigin)
        {
            throw new FormatException(
                $"has frame \"{name}\" at ({x}, {y}). A region starts inside its texture, and x and y are not negative. Move the frame inside the texture.");
        }

        if (width < SheetJson.LeastExtent || height < SheetJson.LeastExtent)
        {
            throw new FormatException(
                $"has frame \"{name}\" {width}x{height}. A region has at least one texel on each axis. Give it a positive width and height.");
        }

        (float pivotX, float pivotY) = frame.Pivot is { } pivot ? Point(pivot, name, "a pivot") : (0F, 0F);

        return new SheetFrame(name, x, y, width, height, pivotX, pivotY, sockets, boxes);
    }

    private static void RequireAllSet(string[] declared, bool[] set, string kind, string shape)
    {
        int unset = Array.IndexOf(set, false);
        if (unset >= 0)
        {
            throw new FormatException(
                $"declares {kind} \"{declared[unset]}\", which no frame sets. A {kind} is {shape} on the frames that carry it. Set it on a frame, or remove its declaration.");
        }
    }

    // A frame sets the sockets or boxes it has a value for and leaves the rest out. They are emitted in
    // the order the sheet declares them, whatever order the frame listed them in.
    private static TMark[] FrameMarks<TJson, TMark>(
        Dictionary<string, TJson>? values,
        string name,
        string[] declared,
        bool[] set,
        string kind,
        string list,
        Func<TJson, string, string, TMark> read)
    {
        if (values is not { Count: > 0 })
        {
            return [];
        }

        List<TMark> marks = new(values.Count);

        for (int i = 0; i < declared.Length; i++)
        {
            if (values.TryGetValue(declared[i], out TJson? value))
            {
                marks.Add(read(value, name, declared[i]));
                set[i] = true;
            }
        }

        if (marks.Count != values.Count)
        {
            foreach (string mark in values.Keys)
            {
                if (Array.IndexOf(declared, mark) < 0)
                {
                    throw new FormatException(
                        $"has frame \"{name}\" setting {kind} \"{mark}\", which the sheet does not declare. Every {kind} a frame sets is named in the sheet's {list} list. Declare it there, or remove it from the frame.");
                }
            }
        }

        return [.. marks];
    }

    private static SheetSocket Socket(float[] point, string frame, string socket)
    {
        (float x, float y) = Point(point, frame, $"socket \"{socket}\"");

        return new SheetSocket(socket, x, y);
    }

    // A box is x, y, width and height in texels of the frame from its top-left corner. It may reach
    // outside the frame, but it encloses some area.
    private static SheetBox Box(FrameBoxJson? rect, string frame, string box)
    {
        if (rect?.X is not { } x || rect.Y is not { } y || rect.Width is not { } width || rect.Height is not { } height)
        {
            string missing = rect?.X is null ? "x" : rect.Y is null ? "y" : rect.Width is null ? "width" : "height";
            throw new FormatException(
                $"has frame \"{frame}\" with box \"{box}\" and no {missing}. A box carries x, y, width and height in texels of the frame. Add the missing one.");
        }

        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height))
        {
            throw new FormatException(
                $"has frame \"{frame}\" with box \"{box}\" that is not finite. A box's x, y, width and height are texel offsets and extents. Write each as a finite number.");
        }

        return width > SheetJson.BoxExtentAbove && height > SheetJson.BoxExtentAbove
            ? new SheetBox(box, x, y, width, height)
            : throw new FormatException(
                $"has frame \"{frame}\" with box \"{box}\" {width}x{height}. A box has a positive width and height. Give it some area.");
    }

    private static SheetClip[] Clips(List<ClipJson>? declared, SheetFrame[] frames, string[] events)
    {
        // A sheet may declare frames only. A static sprite is one frame drawn with no animator.
        bool[] raised = new bool[events.Length];
        SheetClip[] clips = declared is { Count: > 0 } ? ReadClips(declared, frames, events, raised) : [];

        for (int i = 0; i < events.Length; i++)
        {
            if (!raised[i])
            {
                throw new FormatException(
                    $"declares event \"{events[i]}\", which no clip entry raises. An event is raised by the entries that list it. List it on an entry, or remove its declaration.");
            }
        }

        return clips;
    }

    private static SheetClip[] ReadClips(List<ClipJson> declared, SheetFrame[] frames, string[] events, bool[] raised)
    {
        HashSet<string> frameNames = frames.Select(static frame => frame.Name).ToHashSet(StringComparer.Ordinal);

        SheetClip[] clips = new SheetClip[declared.Count];

        // Frames and clips are separate name spaces. A frame and a clip may share a name.
        Names named = new(SheetMembers.ClipsClass);

        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i] is not { } clip)
            {
                throw new FormatException($"has clips[{i}] as null. Every clip is an object. Write the clip, or remove the null.");
            }

            string name = named.Read(clip.Name, $"clips[{i}]");

            if (clip.Frames is not { Count: >= SheetJson.NonEmpty } played)
            {
                throw new FormatException($"has clip \"{name}\" with no frames. A clip plays at least one frame. Add an entry to its \"frames\".");
            }

            SheetClipFrame[] sequence = new SheetClipFrame[played.Count];
            for (int j = 0; j < played.Count; j++)
            {
                ClipFrameJson? entry = played[j];

                if (entry?.Frame is not { Length: >= SheetJson.NonEmpty } frame || !frameNames.Contains(frame))
                {
                    throw new FormatException(
                        $"has clip \"{name}\" frame {j} playing \"{entry?.Frame}\", which this sheet has no frame named. A clip plays frames of its own sheet. Name one of this sheet's frames.");
                }

                if (entry.Ticks is not { } ticks)
                {
                    throw new FormatException(
                        $"has clip \"{name}\" frame {j} with no ticks. Every entry states how many fixed steps its frame is held for. Add \"ticks\".");
                }

                if (ticks < SheetJson.LeastTicks)
                {
                    throw new FormatException(
                        $"has clip \"{name}\" frame {j} held for {ticks} ticks. A frame is held for at least one fixed step, and a duration counts fixed steps, not milliseconds. Set \"ticks\" to 1 or more.");
                }

                sequence[j] = new SheetClipFrame(frame, ticks, EntryEvents(entry.Events, name, j, events, raised));
            }

            clips[i] = new SheetClip(name, clip.Loop ?? false, sequence);
        }

        return clips;
    }

    // An entry lists the declared events it raises, each once, kept in the order it lists them.
    private static string[] EntryEvents(List<string>? listed, string clip, int entry, string[] declared, bool[] raised)
    {
        if (listed is not { Count: > 0 })
        {
            return [];
        }

        string[] names = new string[listed.Count];

        for (int i = 0; i < listed.Count; i++)
        {
            string? name = listed[i];
            int index = name is null ? -1 : Array.IndexOf(declared, name);
            if (index < 0)
            {
                throw new FormatException(
                    $"has clip \"{clip}\" frame {entry} raising event \"{name}\", which the sheet does not declare. Every event an entry raises is named in the sheet's events list. Declare it there, or remove it from the entry.");
            }

            if (Array.IndexOf(names, name, 0, i) >= 0)
            {
                throw new FormatException(
                    $"has clip \"{clip}\" frame {entry} raising event \"{name}\" twice. An entry lists each event once. Remove the repeat.");
            }

            raised[index] = true;
            names[i] = declared[index];
        }

        return names;
    }

    private static string Missing(FrameJson frame) =>
        frame.X is null ? "x" : frame.Y is null ? "y" : frame.Width is null ? "width" : "height";

    // A pivot or a socket: [x, y] in texels of the frame from its top-left corner. A JSON number too
    // large for a float reads as an infinity, which is no texel offset.
    private static (float X, float Y) Point(float[] point, string frame, string what)
    {
        if (point.Length != SheetJson.PointLength)
        {
            throw new FormatException(
                $"has frame \"{frame}\" with {what} of {point.Length} components. A point on a frame is written [x, y] in texels from its top-left corner. Write it as two numbers.");
        }

        return float.IsFinite(point[0]) && float.IsFinite(point[1])
            ? (point[0], point[1])
            : throw new FormatException($"has frame \"{frame}\" with {what} that is not finite. A point on a frame is a pair of texel offsets. Write each as a finite number.");
    }

    // One rule for every name space: non-empty, unique, an identifier, and not the name of the
    // generated class the member is declared on, which C# refuses (CS0542).
    private sealed class Names(string reserved)
    {
        private readonly HashSet<string> _byName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byIdentifier = new(StringComparer.Ordinal);

        internal string Read(string? authored, string position)
        {
            if (authored is not { Length: > 0 } name)
            {
                throw new FormatException($"has {position} with no name. Every frame, clip, socket, box and event is named. Give it a \"name\".");
            }

            if (!_byName.Add(name))
            {
                throw new FormatException(
                    $"has {position} as a second \"{name}\". A game reaches each by name, and names are unique within their list. Rename one.");
            }

            if (AssetPaths.ToIdentifier(name) is not { } identifier)
            {
                throw new FormatException(
                    $"has {position} named \"{name}\", which is no C# name. A name is letters, digits, '-' and '_', and does not start with a digit. Rename it.");
            }

            if (string.Equals(identifier, reserved, StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"has {position} named \"{name}\", which is the generated '{reserved}' class it would be declared on. Rename it.");
            }

            if (_byIdentifier.TryGetValue(identifier, out string? claimed))
            {
                throw new FormatException(
                    $"has {position} named \"{name}\" where \"{claimed}\" is already declared as '{identifier}'. Two names differing only in their separators are one C# name. Rename one.");
            }

            _byIdentifier[identifier] = name;

            return name;
        }
    }
}
