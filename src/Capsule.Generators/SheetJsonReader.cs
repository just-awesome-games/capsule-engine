using System.Globalization;
using System.Text;
using Capsule.Assets;

namespace Capsule.Generators;

// One frame as the sheet declares it, in plain numbers: the generator runs in the analyzer process
// and names no Capsule.Core type, so the region and pivot are carried apart and become literals.
internal sealed class SheetFrame(string name, int x, int y, int width, int height, float pivotX, float pivotY, SheetSocket[] sockets)
{
    internal readonly string Name = name;
    internal readonly int X = x;
    internal readonly int Y = y;
    internal readonly int Width = width;
    internal readonly int Height = height;
    internal readonly float PivotX = pivotX;
    internal readonly float PivotY = pivotY;
    internal readonly SheetSocket[] Sockets = sockets;
}

// One socket a frame sets, in the pivot's texel space.
internal sealed class SheetSocket(string name, float x, float y)
{
    internal readonly string Name = name;
    internal readonly float X = x;
    internal readonly float Y = y;
}

internal sealed class SheetClipFrame(string frame, int ticks)
{
    internal readonly string Frame = frame;
    internal readonly int Ticks = ticks;
}

internal sealed class SheetClip(string name, bool loop, SheetClipFrame[] frames)
{
    internal readonly string Name = name;
    internal readonly bool Loop = loop;
    internal readonly SheetClipFrame[] Frames = frames;
}

// One sprite sheet the generator read: the texture every frame cuts from, as the key and extension
// the build ships it at, the sockets it names, and the frames and clips declared over it.
internal sealed class SheetDocument(string textureKey, string textureExtension, string[] sockets, SheetFrame[] frames, SheetClip[] clips)
{
    internal readonly string TextureKey = textureKey;
    internal readonly string TextureExtension = textureExtension;
    internal readonly string[] Sockets = sockets;
    internal readonly SheetFrame[] Frames = frames;
    internal readonly SheetClip[] Clips = clips;

    /// <summary>The texture's path under the textures root, extension included.</summary>
    internal string Texture => TextureKey + TextureExtension;
}

// Reads the sprite sheet document format. The whole sheet is known at compile time, so the
// generated registry carries it as literal data and no document ships or is parsed at run time.
// The JSON is read by hand: the analyzer process carries no serializer, and a strict reader is what
// refuses the member a typo invents.
internal static class SheetJsonReader
{
    /// <summary>The extension a sheet document is authored under, both halves of it.</summary>
    internal const string SheetExtension = ".sheet.json";

    private const int FormatVersion = 1;

    /// <summary>
    /// Reads <paramref name="text"/>. Null with <paramref name="error"/> set when the JSON is
    /// malformed or the document breaks the format; the message states the defect.
    /// </summary>
    /// <param name="errorLine">The zero-based line the defect is on.</param>
    internal static SheetDocument? Parse(string text, out string? error, out int errorLine)
    {
        try
        {
            RawSheet raw = new Reader(text).ReadSheet();

            error = null;
            errorLine = 0;

            return Validate(raw);
        }
        catch (SheetFormatException ex)
        {
            error = ex.Message;
            errorLine = LineOf(text, ex.Index);

            return null;
        }
    }

    private static int LineOf(string text, int index)
    {
        int line = 0;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static SheetDocument Validate(RawSheet raw)
    {
        if (raw.FormatVersion.Value is not { } formatVersion)
        {
            throw new SheetFormatException(
                Invariant($"has no formatVersion; this build supports formatVersion {FormatVersion}."),
                raw.Index);
        }

        if (formatVersion != FormatVersion)
        {
            throw new SheetFormatException(
                Invariant($"declares formatVersion {formatVersion}, which is unsupported; this build supports formatVersion {FormatVersion}."),
                raw.FormatVersion.Index);
        }

        (string key, string extension) = Texture(raw);
        RawSocket[] sockets = Sockets(raw);
        SheetFrame[] frames = Frames(raw, sockets);

        return new SheetDocument(key, extension, SocketNames(sockets), frames, Clips(raw, frames));
    }

    // The declarations alone; whether each is set by a frame is known only once the frames are read.
    private static RawSocket[] Sockets(RawSheet raw)
    {
        if (raw.Sockets is not { Count: > 0 } entries)
        {
            return [];
        }

        // Its own name space: a socket may share a name with a frame or a clip.
        Names named = new(SpriteRegistrySource.SocketsClass);
        RawSocket[] sockets = new RawSocket[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            RawSocket entry = entries[i];
            named.Read(entry.Name.Value, $"sockets[{i}]", entry.Name.Index, entry.Index);
            sockets[i] = entry;
        }

        return sockets;
    }

    private static string[] SocketNames(RawSocket[] sockets)
    {
        string[] names = new string[sockets.Length];
        for (int i = 0; i < sockets.Length; i++)
        {
            names[i] = sockets[i].Name.Value!;
        }

        return names;
    }

    private static (string Key, string Extension) Texture(RawSheet raw)
    {
        if (raw.Texture.Value is not { Length: > 0 } path)
        {
            throw new SheetFormatException(
                "names no texture; a sheet cuts its frames from one texture under assets/textures.",
                raw.Texture.Index is 0 ? raw.Index : raw.Texture.Index);
        }

        if (!AssetPaths.TrySplit(path, out string name, out string extension))
        {
            throw new SheetFormatException(
                $"has texture \"{path}\"; a texture is one asset's path under assets/textures, extension included — \"player.png\" at the root, \"actors/player.png\" below it — with forward slashes and no empty, \".\" or \"..\" segment.",
                raw.Texture.Index);
        }

        // However the document spelled it, a texture is reached by its key: the handle this emits
        // is the one the build ships the texture under.
        return TypeNaming.NormalizeKey(name, out string? rejected) is { } key
            ? (key, extension)
            : throw new SheetFormatException(
                $"has texture \"{path}\", whose \"{rejected}\" is no C# name; every segment of a texture path is letters, digits, '-' and '_', and does not start with a digit.",
                raw.Texture.Index);
    }

    private static SheetFrame[] Frames(RawSheet raw, RawSocket[] sockets)
    {
        if (raw.Frames.Value is not { } entries)
        {
            throw new SheetFormatException(
                "has no frames; a sheet names at least one region of its texture.",
                raw.Index);
        }

        if (entries.Count == 0)
        {
            throw new SheetFormatException(
                "has an empty frames list; a sheet names at least one region of its texture.",
                raw.Frames.Index);
        }

        SheetFrame[] frames = new SheetFrame[entries.Count];
        Names named = new(SpriteRegistrySource.FramesClass);

        // Which declared sockets some frame set, by declaration index.
        bool[] set = new bool[sockets.Length];

        for (int i = 0; i < entries.Count; i++)
        {
            RawFrame entry = entries[i];
            string name = named.Read(entry.Name.Value, $"frames[{i}]", entry.Name.Index, entry.Index);

            if (entry.X is not { } x || entry.Y is not { } y || entry.Width is not { } width || entry.Height is not { } height)
            {
                throw new SheetFormatException(
                    $"has frame \"{name}\" with no {Missing(entry)}; every frame carries x, y, width and height in texels.",
                    entry.Index);
            }

            if (x < 0 || y < 0)
            {
                throw new SheetFormatException(
                    Invariant($"has frame \"{name}\" at ({x}, {y}); a region starts inside its texture, so x and y are not negative."),
                    entry.Index);
            }

            if (width <= 0 || height <= 0)
            {
                throw new SheetFormatException(
                    Invariant($"has frame \"{name}\" {width}x{height}; a region has at least one texel on each axis."),
                    entry.Index);
            }

            (float pivotX, float pivotY) = Pivot(entry, name);
            frames[i] = new SheetFrame(name, x, y, width, height, pivotX, pivotY, FrameSockets(entry, name, sockets, set));
        }

        for (int i = 0; i < sockets.Length; i++)
        {
            if (!set[i])
            {
                throw new SheetFormatException(
                    $"declares socket \"{sockets[i].Name.Value}\", which no frame sets; a socket is a point on the frames that carry it, so at least one sets it or the declaration goes.",
                    sockets[i].Index);
            }
        }

        return frames;
    }

    // A frame's sockets, each a declared name with a finite point in the pivot's texel space. Sparse
    // is the model: a frame sets the sockets it has a point for and leaves the rest out.
    private static SheetSocket[] FrameSockets(RawFrame entry, string frame, RawSocket[] declared, bool[] set)
    {
        if (entry.Sockets.Value is not { Count: > 0 } entries)
        {
            return [];
        }

        SheetSocket[] sockets = new SheetSocket[entries.Count];

        for (int i = 0; i < entries.Count; i++)
        {
            RawFrameSocket socket = entries[i];
            int index = IndexOf(declared, socket.Name);

            if (index < 0)
            {
                throw new SheetFormatException(
                    $"has frame \"{frame}\" setting socket \"{socket.Name}\", which the sheet does not declare; every socket a frame sets is named in the sheet's sockets list.",
                    socket.Index);
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(sockets[j].Name, socket.Name, StringComparison.Ordinal))
                {
                    throw new SheetFormatException(
                        $"has frame \"{frame}\" setting socket \"{socket.Name}\" twice; a frame sets each socket once.",
                        socket.Index);
                }
            }

            if (socket.Point.Count != 2)
            {
                throw new SheetFormatException(
                    Invariant($"has frame \"{frame}\" setting socket \"{socket.Name}\" with {socket.Point.Count} components; a socket is written [x, y] in texels of the frame from its top-left corner, as a pivot is."),
                    socket.Index);
            }

            if (!IsFinite(socket.Point[0]) || !IsFinite(socket.Point[1]))
            {
                throw new SheetFormatException(
                    $"has frame \"{frame}\" setting socket \"{socket.Name}\" to a point that is not finite; a socket is a pair of texel offsets.",
                    socket.Index);
            }

            set[index] = true;
            sockets[i] = new SheetSocket(declared[index].Name.Value!, socket.Point[0], socket.Point[1]);
        }

        return sockets;
    }

    private static int IndexOf(RawSocket[] declared, string name)
    {
        for (int i = 0; i < declared.Length; i++)
        {
            if (string.Equals(declared[i].Name.Value, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static SheetClip[] Clips(RawSheet raw, SheetFrame[] frames)
    {
        // A sheet of frames only: a static sprite is one frame a renderer draws with no animator.
        if (raw.Clips is not { Count: > 0 } entries)
        {
            return [];
        }

        HashSet<string> frameNames = new(StringComparer.Ordinal);
        foreach (SheetFrame frame in frames)
        {
            frameNames.Add(frame.Name);
        }

        SheetClip[] clips = new SheetClip[entries.Count];

        // Frames and clips are separate name spaces, so a frame and a clip may share a name.
        Names named = new(SpriteRegistrySource.ClipsClass);

        for (int i = 0; i < entries.Count; i++)
        {
            RawClip entry = entries[i];
            string name = named.Read(entry.Name.Value, $"clips[{i}]", entry.Name.Index, entry.Index);

            if (entry.Frames is not { Count: > 0 } clipFrames)
            {
                throw new SheetFormatException($"has clip \"{name}\" with no frames; a clip plays at least one.", entry.Index);
            }

            SheetClipFrame[] played = new SheetClipFrame[clipFrames.Count];
            for (int j = 0; j < clipFrames.Count; j++)
            {
                RawClipFrame entryFrame = clipFrames[j];

                if (entryFrame.Frame is not { Length: > 0 } frame || !frameNames.Contains(frame))
                {
                    throw new SheetFormatException(
                        $"has clip \"{name}\" frame {j} playing \"{entryFrame.Frame}\", which this sheet has no frame named; a clip plays frames of its own sheet.",
                        entryFrame.Index);
                }

                if (entryFrame.Ticks is not { } ticks)
                {
                    throw new SheetFormatException(
                        $"has clip \"{name}\" frame {j} with no ticks; every entry states how many fixed steps its frame is held for.",
                        entryFrame.Index);
                }

                if (ticks <= 0)
                {
                    throw new SheetFormatException(
                        Invariant($"has clip \"{name}\" frame {j} held for {ticks} ticks; a frame is held for at least one fixed step, and durations are ticks of the game's fixed step rather than milliseconds."),
                        entryFrame.Index);
                }

                played[j] = new SheetClipFrame(frame, ticks);
            }

            clips[i] = new SheetClip(name, entry.Loop ?? false, played);
        }

        return clips;
    }

    private static string Missing(RawFrame entry) =>
        entry.X is null ? "x" : entry.Y is null ? "y" : entry.Width is null ? "width" : "height";

    private static (float X, float Y) Pivot(RawFrame entry, string name)
    {
        if (entry.Pivot.Value is not { } pivot)
        {
            return (0F, 0F);
        }

        if (pivot.Count != 2)
        {
            throw new SheetFormatException(
                Invariant($"has frame \"{name}\" with a pivot of {pivot.Count} components; a pivot is written [x, y] in texels of the frame from its top-left corner, and a frame anchored at that corner leaves it out."),
                entry.Pivot.Index);
        }

        return !IsFinite(pivot[0]) || !IsFinite(pivot[1])
            ? throw new SheetFormatException(
                $"has frame \"{name}\" with a pivot that is not finite; a pivot is a pair of texel offsets.",
                entry.Pivot.Index)
            : (pivot[0], pivot[1]);
    }

    // netstandard2.0 carries no float.IsFinite.
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static string Invariant(FormattableString message) => FormattableString.Invariant(message);

    // One rule for both name spaces: non-empty, unique, an identifier, and not the name of the
    // generated class the member is declared on — a member may not carry that (CS0542).
    private sealed class Names(string reserved)
    {
        private readonly HashSet<string> _byName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byIdentifier = new(StringComparer.Ordinal);

        internal string Read(string? authored, string position, int nameIndex, int index)
        {
            if (authored is not { Length: > 0 } name)
            {
                throw new SheetFormatException($"has {position} with no name; every frame, clip and socket is named.", index);
            }

            if (!_byName.Add(name))
            {
                throw new SheetFormatException(
                    $"has {position} as a second \"{name}\"; names are unique within their list, since a game reaches each by name.",
                    nameIndex);
            }

            if (TypeNaming.ToIdentifier(name) is not { } identifier)
            {
                throw new SheetFormatException(
                    $"has {position} named \"{name}\", which is no C# name; a name is letters, digits, '-' and '_', and does not start with a digit.",
                    nameIndex);
            }

            if (string.Equals(identifier, reserved, StringComparison.Ordinal))
            {
                throw new SheetFormatException(
                    $"has {position} named \"{name}\", which is the generated '{reserved}' class it would be declared on; name it something else.",
                    nameIndex);
            }

            if (_byIdentifier.TryGetValue(identifier, out string? claimed))
            {
                throw new SheetFormatException(
                    $"has {position} named \"{name}\" where \"{claimed}\" is already declared as '{identifier}'; two names that differ only in their separators are one C# name.",
                    nameIndex);
            }

            _byIdentifier[identifier] = name;

            return name;
        }
    }

    // The document as the JSON spelled it, before any rule is applied to it: every member is
    // optional here so a missing one is refused by name rather than as a parse failure.
    // One member the JSON may or may not carry, paired with the byte it started at: what a message
    // about that member points to, and the object's own start where it was never declared.
    private readonly struct Member<T>(int index, T value)
    {
        internal int Index { get; } = index;

        internal T Value { get; } = value;
    }

    private sealed class RawSheet
    {
        internal int Index;
        internal Member<int?> FormatVersion;
        internal Member<string?> Texture;
        internal List<RawSocket>? Sockets;
        internal Member<List<RawFrame>?> Frames;
        internal List<RawClip>? Clips;
    }

    private sealed class RawSocket
    {
        internal int Index;
        internal Member<string?> Name;
    }

    private sealed class RawFrame
    {
        internal int Index;
        internal Member<string?> Name;
        internal int? X;
        internal int? Y;
        internal int? Width;
        internal int? Height;
        internal Member<List<float>?> Pivot;
        internal Member<List<RawFrameSocket>?> Sockets;
    }

    // One entry of a frame's sockets object: the key and the point it maps to.
    private sealed class RawFrameSocket(int index, string name, List<float> point)
    {
        internal readonly int Index = index;
        internal readonly string Name = name;
        internal readonly List<float> Point = point;
    }

    private sealed class RawClip
    {
        internal int Index;
        internal Member<string?> Name;
        internal bool? Loop;
        internal List<RawClipFrame>? Frames;
    }

    private sealed class RawClipFrame
    {
        internal int Index;
        internal string? Frame;
        internal int? Ticks;
    }

    private sealed class SheetFormatException(string message, int index) : Exception(message)
    {
        internal int Index { get; } = index;
    }

    // A strict recursive-descent reader over the one shape this format has: a member the format
    // does not declare fails the sheet, which is what a typo looks like.
    private sealed class Reader(string text)
    {
        private int _index;

        internal RawSheet ReadSheet()
        {
            SkipSpace();

            if (_index >= text.Length)
            {
                throw Fail("is empty; a sheet document is one JSON object.");
            }

            RawSheet sheet = new() { Index = _index };

            ReadObject(name =>
            {
                switch (name)
                {
                    case "formatVersion":
                        sheet.FormatVersion = new(_index, ReadInt("formatVersion"));
                        break;

                    case "texture":
                        sheet.Texture = new(_index, ReadString());
                        break;

                    case "sockets":
                        sheet.Sockets = TryNull() ? null : ReadArray(ReadSocket);
                        break;

                    case "frames":
                        sheet.Frames = new(_index, ReadArray(ReadFrame));
                        break;

                    case "clips":
                        sheet.Clips = TryNull() ? null : ReadArray(ReadClip);
                        break;

                    case "source":
                        // Accepted so a derived sheet may name what it came from; nothing reads it.
                        if (!TryNull())
                        {
                            ReadObject(member =>
                            {
                                if (member is not ("tool" or "path" or "hash"))
                                {
                                    throw Unknown(member, "a source block");
                                }

                                if (!TryNull())
                                {
                                    ReadString();
                                }
                            });
                        }

                        break;

                    default:
                        throw Unknown(name, "the sheet format");
                }
            });

            SkipSpace();

            if (_index < text.Length)
            {
                throw Fail("carries text after the sheet object; a sheet document is one JSON object.");
            }

            return sheet;
        }

        private RawSocket ReadSocket()
        {
            RawSocket socket = new() { Index = _index };

            ReadObject(name =>
            {
                if (name is not "name")
                {
                    throw Unknown(name, "a socket");
                }

                socket.Name = new(_index, ReadString());
            });

            return socket;
        }

        private RawFrame ReadFrame()
        {
            RawFrame frame = new() { Index = _index };

            ReadObject(name =>
            {
                switch (name)
                {
                    case "name":
                        frame.Name = new(_index, ReadString());
                        break;

                    case "x":
                        frame.X = ReadInt("x");
                        break;
                    case "y":
                        frame.Y = ReadInt("y");
                        break;
                    case "width":
                        frame.Width = ReadInt("width");
                        break;
                    case "height":
                        frame.Height = ReadInt("height");
                        break;

                    case "pivot":
                        frame.Pivot = new(_index, TryNull() ? null : ReadArray(ReadFloat));
                        break;

                    case "sockets":
                        frame.Sockets = new(_index, TryNull() ? null : ReadFrameSockets());
                        break;

                    default:
                        throw Unknown(name, "a frame");
                }
            });

            return frame;
        }

        // An object keyed by socket name, each value a point; the key is the entry's anchor.
        private List<RawFrameSocket> ReadFrameSockets()
        {
            List<RawFrameSocket> sockets = [];

            ReadObject(name => sockets.Add(new RawFrameSocket(_index, name, ReadArray(ReadFloat))));

            return sockets;
        }

        private RawClip ReadClip()
        {
            RawClip clip = new() { Index = _index };

            ReadObject(name =>
            {
                switch (name)
                {
                    case "name":
                        clip.Name = new(_index, ReadString());
                        break;

                    case "loop":
                        clip.Loop = TryNull() ? null : ReadBool();
                        break;
                    case "frames":
                        clip.Frames = ReadArray(ReadClipFrame);
                        break;

                    default:
                        throw Unknown(name, "a clip");
                }
            });

            return clip;
        }

        private RawClipFrame ReadClipFrame()
        {
            RawClipFrame entry = new() { Index = _index };

            ReadObject(name =>
            {
                switch (name)
                {
                    case "frame":
                        entry.Frame = ReadString();
                        break;
                    case "ticks":
                        entry.Ticks = ReadInt("ticks");
                        break;

                    default:
                        throw Unknown(name, "a clip frame");
                }
            });

            return entry;
        }

        private void ReadObject(Action<string> member)
        {
            Expect('{');
            SkipSpace();

            if (Peek() == '}')
            {
                _index++;

                return;
            }

            while (true)
            {
                SkipSpace();
                string name = ReadString();
                SkipSpace();
                Expect(':');
                SkipSpace();
                member(name);
                SkipSpace();

                char next = Expect('}', ',');
                if (next == '}')
                {
                    return;
                }
            }
        }

        private List<T> ReadArray<T>(Func<T> element)
        {
            List<T> values = [];

            Expect('[');
            SkipSpace();

            if (Peek() == ']')
            {
                _index++;

                return values;
            }

            while (true)
            {
                SkipSpace();
                values.Add(element());
                SkipSpace();

                if (Expect(']', ',') == ']')
                {
                    return values;
                }
            }
        }

        private string ReadString()
        {
            Expect('"');
            StringBuilder value = new();

            while (true)
            {
                if (_index >= text.Length)
                {
                    throw Fail("ends inside a string; a string is closed with '\"'.");
                }

                char character = text[_index++];

                if (character == '"')
                {
                    return value.ToString();
                }

                if (character < ' ')
                {
                    throw Fail("carries a control character inside a string; JSON writes one as an escape.");
                }

                if (character != '\\')
                {
                    value.Append(character);
                    continue;
                }

                if (_index >= text.Length)
                {
                    throw Fail("ends inside a string escape.");
                }

                char escaped = text[_index++];
                switch (escaped)
                {
                    case '"':
                        value.Append('"');
                        break;
                    case '\\':
                        value.Append('\\');
                        break;
                    case '/':
                        value.Append('/');
                        break;
                    case 'b':
                        value.Append('\b');
                        break;
                    case 'f':
                        value.Append('\f');
                        break;
                    case 'n':
                        value.Append('\n');
                        break;
                    case 'r':
                        value.Append('\r');
                        break;
                    case 't':
                        value.Append('\t');
                        break;

                    case 'u':
                        // Checked digit by digit: the hex conversion below takes surrounding space.
                        if (_index + 4 > text.Length
                            || !IsHex(text[_index]) || !IsHex(text[_index + 1])
                            || !IsHex(text[_index + 2]) || !IsHex(text[_index + 3]))
                        {
                            throw Fail("carries a \\u escape that is no four hexadecimal digits.");
                        }

                        value.Append((char)ushort.Parse(
                            text.Substring(_index, 4),
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture));
                        _index += 4;
                        break;

                    default:
                        throw Fail($"carries the escape \"\\{escaped}\", which JSON does not declare.");
                }
            }
        }

        private int ReadInt(string member)
        {
            int start = _index;
            string number = ReadNumber();

            return int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
                ? value
                : throw new SheetFormatException(
                    $"declares {member} as {number}, which is no whole number.",
                    start);
        }

        private float ReadFloat()
        {
            int start = _index;
            string number = ReadNumber();

            return float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value
                : throw new SheetFormatException($"declares {number}, which is no number.", start);
        }

        // RFC 8259's number: -?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?. Spelling a leading '+',
        // a leading zero or a bare '.' out of the span is what refuses them, since the conversions
        // this hands the span to would take several of them.
        private string ReadNumber()
        {
            int start = _index;

            if (Peek() == '-')
            {
                _index++;
            }

            if (Peek() == '0')
            {
                _index++;
            }
            else
            {
                ReadDigits(start);
            }

            if (Peek() == '.')
            {
                _index++;
                ReadDigits(start);
            }

            if (Peek() is 'e' or 'E')
            {
                _index++;

                if (Peek() is '+' or '-')
                {
                    _index++;
                }

                ReadDigits(start);
            }

            return text.Substring(start, _index - start);
        }

        private void ReadDigits(int start)
        {
            int first = _index;

            while (_index < text.Length && text[_index] is >= '0' and <= '9')
            {
                _index++;
            }

            if (_index == first)
            {
                throw Fail($"carries {Around(start)} where a number was expected.");
            }
        }

        private static bool IsHex(char character) =>
            character is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

        private bool ReadBool()
        {
            if (Literal("true"))
            {
                return true;
            }

            return Literal("false") ? false : throw Fail($"carries {Around(_index)} where true or false was expected.");
        }

        // A null where the format declares an optional member is what leaving it out is.
        private bool TryNull() => Literal("null");

        private bool Literal(string word)
        {
            if (_index + word.Length > text.Length
                || string.CompareOrdinal(text, _index, word, 0, word.Length) != 0)
            {
                return false;
            }

            _index += word.Length;

            return true;
        }

        private char Expect(char first, char second = '\0')
        {
            SkipSpace();

            char character = Peek();
            if (character == first || (second != '\0' && character == second))
            {
                _index++;

                return character;
            }

            string expected = second == '\0' ? $"'{first}'" : $"'{first}' or '{second}'";

            throw Fail($"carries {Around(_index)} where {expected} was expected.");
        }

        private char Peek() => _index < text.Length ? text[_index] : '\0';

        private void SkipSpace()
        {
            // JSON's four, never char.IsWhiteSpace's set: a no-break space between tokens is text.
            while (_index < text.Length && text[_index] is ' ' or '\t' or '\n' or '\r')
            {
                _index++;
            }
        }

        private string Around(int index) =>
            index >= text.Length
                ? "an end of file"
                : "\"" + text.Substring(index, Math.Min(8, text.Length - index)).Replace("\n", " ").Replace("\r", " ") + "\"";

        private SheetFormatException Unknown(string name, string shape) =>
            new($"declares \"{name}\", which {shape} has no member named; the document is malformed.", _index);

        private SheetFormatException Fail(string message) => new(message, _index);
    }
}
