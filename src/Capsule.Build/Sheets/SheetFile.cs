using Capsule.Assets;
using Capsule.Build.Configuration;

namespace Capsule.Build.Sheets;

/// <summary>
/// Reads the sheet document format. The sheet is known at build time. The generated registry
/// carries it as literal data, and no document ships or is parsed at run time.
/// </summary>
internal static class SheetFile
{
    internal const int SupportedFormat = 1;

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
            throw new FormatException($"has no formatVersion. This build supports formatVersion {SupportedFormat}.");
        }

        if (format != SupportedFormat)
        {
            throw new FormatException(
                $"declares formatVersion {format}, which is unsupported. This build supports formatVersion {SupportedFormat}.");
        }

        (string key, string extension) = Texture(json.Texture);
        // Sockets, boxes and events each have their own name space. One may share a name with a frame,
        // a clip or a member of another list.
        string[] sockets = Declared(json.Sockets?.ConvertAll(static socket => socket?.Name), SheetMembers.SocketsClass, "sockets");
        string[] boxes = Declared(json.Boxes?.ConvertAll(static box => box?.Name), SheetMembers.BoxesClass, "boxes");
        string[] events = Declared(json.Events?.ConvertAll(static raised => raised?.Name), SheetMembers.EventsClass, "events");
        SheetFrame[] frames = Frames(json.Frames, sockets, boxes);

        return new Sheet(key, extension, sockets, boxes, events, frames, Clips(json.Clips, frames, events));
    }

    private static (string Key, string Extension) Texture(string? path)
    {
        if (path is not { Length: > 0 })
        {
            throw new FormatException("names no texture. A sheet cuts its frames from one texture.");
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
        if (declared is not { Count: > 0 })
        {
            throw new FormatException("has no frames. A sheet names at least one region of its texture.");
        }

        SheetFrame[] frames = new SheetFrame[declared.Count];
        Names named = new(SheetMembers.FramesClass);
        bool[] set = new bool[sockets.Length];
        bool[] boxesSet = new bool[boxes.Length];

        for (int i = 0; i < declared.Count; i++)
        {
            if (declared[i] is not { } frame)
            {
                throw new FormatException($"has frames[{i}] as null. Every frame is an object.");
            }

            string name = named.Read(frame.Name, $"frames[{i}]");

            if (frame.X is not { } x || frame.Y is not { } y || frame.Width is not { } width || frame.Height is not { } height)
            {
                throw new FormatException(
                    $"has frame \"{name}\" with no {Missing(frame)}. Every frame carries x, y, width and height in texels.");
            }

            if (x < 0 || y < 0)
            {
                throw new FormatException(
                    $"has frame \"{name}\" at ({x}, {y}). A region starts inside its texture, so x and y are not negative.");
            }

            if (width <= 0 || height <= 0)
            {
                throw new FormatException(
                    $"has frame \"{name}\" {width}x{height}. A region has at least one texel on each axis.");
            }

            (float pivotX, float pivotY) = frame.Pivot is { } pivot ? Point(pivot, name, "a pivot") : (0F, 0F);
            frames[i] = new SheetFrame(
                name,
                x,
                y,
                width,
                height,
                pivotX,
                pivotY,
                FrameSockets(frame, name, sockets, set),
                FrameBoxes(frame, name, boxes, boxesSet));
        }

        for (int i = 0; i < sockets.Length; i++)
        {
            if (!set[i])
            {
                throw new FormatException(
                    $"declares socket \"{sockets[i]}\", which no frame sets. A socket is a point on the frames that carry it, so either a frame sets it or the declaration goes.");
            }
        }

        for (int i = 0; i < boxes.Length; i++)
        {
            if (!boxesSet[i])
            {
                throw new FormatException(
                    $"declares box \"{boxes[i]}\", which no frame sets. A box is a rect on the frames that carry it, so either a frame sets it or the declaration goes.");
            }
        }

        return frames;
    }

    // A frame sets the sockets it has a point for and leaves the rest out. They are emitted in the
    // order the sheet declares them, whatever order the frame listed them in.
    private static SheetSocket[] FrameSockets(FrameJson frame, string name, string[] declared, bool[] set)
    {
        if (frame.Sockets is not { Count: > 0 } points)
        {
            return [];
        }

        List<SheetSocket> sockets = new(points.Count);

        for (int i = 0; i < declared.Length; i++)
        {
            if (!points.TryGetValue(declared[i], out float[]? point))
            {
                continue;
            }

            (float x, float y) = Point(point, name, $"socket \"{declared[i]}\"");
            set[i] = true;
            sockets.Add(new SheetSocket(declared[i], x, y));
        }

        if (sockets.Count != points.Count)
        {
            foreach (string point in points.Keys)
            {
                if (Array.IndexOf(declared, point) < 0)
                {
                    throw new FormatException(
                        $"has frame \"{name}\" setting socket \"{point}\", which the sheet does not declare. Every socket a frame sets is named in the sheet's sockets list.");
                }
            }
        }

        return [.. sockets];
    }

    // A frame sets the boxes it has a rect for. They are emitted in the order the sheet declares them,
    // as sockets are.
    private static SheetBox[] FrameBoxes(FrameJson frame, string name, string[] declared, bool[] set)
    {
        if (frame.Boxes is not { Count: > 0 } rects)
        {
            return [];
        }

        List<SheetBox> boxes = new(rects.Count);

        for (int i = 0; i < declared.Length; i++)
        {
            if (!rects.TryGetValue(declared[i], out FrameBoxJson? rect))
            {
                continue;
            }

            set[i] = true;
            boxes.Add(Box(rect, name, declared[i]));
        }

        if (boxes.Count != rects.Count)
        {
            foreach (string rect in rects.Keys)
            {
                if (Array.IndexOf(declared, rect) < 0)
                {
                    throw new FormatException(
                        $"has frame \"{name}\" setting box \"{rect}\", which the sheet does not declare. Every box a frame sets is named in the sheet's boxes list.");
                }
            }
        }

        return [.. boxes];
    }

    // A box is x, y, width and height in texels of the frame from its top-left corner. It may reach
    // outside the frame, but it encloses some area.
    private static SheetBox Box(FrameBoxJson? rect, string frame, string box)
    {
        if (rect?.X is not { } x || rect.Y is not { } y || rect.Width is not { } width || rect.Height is not { } height)
        {
            string missing = rect?.X is null ? "x" : rect.Y is null ? "y" : rect.Width is null ? "width" : "height";
            throw new FormatException(
                $"has frame \"{frame}\" with box \"{box}\" and no {missing}. A box carries x, y, width and height in texels of the frame.");
        }

        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height))
        {
            throw new FormatException(
                $"has frame \"{frame}\" with box \"{box}\" that is not finite. A box's x, y, width and height are texel offsets and extents.");
        }

        return width > 0F && height > 0F
            ? new SheetBox(box, x, y, width, height)
            : throw new FormatException(
                $"has frame \"{frame}\" with box \"{box}\" {width}x{height}. A box has a positive width and height.");
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
                    $"declares event \"{events[i]}\", which no clip entry raises. An event is raised by the entries that list it, so either an entry lists it or the declaration goes.");
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
                throw new FormatException($"has clips[{i}] as null. Every clip is an object.");
            }

            string name = named.Read(clip.Name, $"clips[{i}]");

            if (clip.Frames is not { Count: > 0 } played)
            {
                throw new FormatException($"has clip \"{name}\" with no frames. A clip plays at least one frame.");
            }

            SheetClipFrame[] sequence = new SheetClipFrame[played.Count];
            for (int j = 0; j < played.Count; j++)
            {
                ClipFrameJson? entry = played[j];

                if (entry?.Frame is not { Length: > 0 } frame || !frameNames.Contains(frame))
                {
                    throw new FormatException(
                        $"has clip \"{name}\" frame {j} playing \"{entry?.Frame}\", which this sheet has no frame named. A clip plays frames of its own sheet.");
                }

                if (entry.Ticks is not { } ticks)
                {
                    throw new FormatException(
                        $"has clip \"{name}\" frame {j} with no ticks. Every entry states how many fixed steps its frame is held for.");
                }

                if (ticks <= 0)
                {
                    throw new FormatException(
                        $"has clip \"{name}\" frame {j} held for {ticks} ticks. A frame is held for at least one fixed step, and a duration counts fixed steps, not milliseconds.");
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
                    $"has clip \"{clip}\" frame {entry} raising event \"{name}\", which the sheet does not declare. Every event an entry raises is named in the sheet's events list.");
            }

            if (Array.IndexOf(names, name, 0, i) >= 0)
            {
                throw new FormatException(
                    $"has clip \"{clip}\" frame {entry} raising event \"{name}\" twice. An entry lists each event once.");
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
        if (point.Length != 2)
        {
            throw new FormatException(
                $"has frame \"{frame}\" with {what} of {point.Length} components. A point on a frame is written [x, y] in texels from its top-left corner.");
        }

        return float.IsFinite(point[0]) && float.IsFinite(point[1])
            ? (point[0], point[1])
            : throw new FormatException($"has frame \"{frame}\" with {what} that is not finite. A point on a frame is a pair of texel offsets.");
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
                throw new FormatException($"has {position} with no name. Every frame, clip, socket, box and event is named.");
            }

            if (!_byName.Add(name))
            {
                throw new FormatException(
                    $"has {position} as a second \"{name}\". Names are unique within their list, since a game reaches each by name.");
            }

            if (AssetPaths.ToIdentifier(name) is not { } identifier)
            {
                throw new FormatException(
                    $"has {position} named \"{name}\", which is no C# name. A name is letters, digits, '-' and '_', and does not start with a digit.");
            }

            if (string.Equals(identifier, reserved, StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"has {position} named \"{name}\", which is the generated '{reserved}' class it would be declared on, so name it something else.");
            }

            if (_byIdentifier.TryGetValue(identifier, out string? claimed))
            {
                throw new FormatException(
                    $"has {position} named \"{name}\" where \"{claimed}\" is already declared as '{identifier}'. Two names differing only in their separators are one C# name.");
            }

            _byIdentifier[identifier] = name;

            return name;
        }
    }
}
