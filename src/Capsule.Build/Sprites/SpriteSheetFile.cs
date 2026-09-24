using System.Text.Json;
using Capsule.Assets;
using Capsule.Generators;

namespace Capsule.Build.Sprites;

/// <summary>One socket a frame sets, in the pivot's texel space.</summary>
internal readonly record struct SheetSocket(string Name, float X, float Y);

/// <summary>One region of the sheet's texture, with the pivot and sockets it carries.</summary>
internal readonly record struct SheetFrame(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    float PivotX,
    float PivotY,
    SheetSocket[] Sockets);

/// <summary>One frame of a clip, held for <paramref name="Ticks"/> fixed steps.</summary>
internal readonly record struct SheetClipFrame(string Frame, int Ticks);

/// <summary>One animation a sheet plays over its own frames.</summary>
internal readonly record struct SheetClip(string Name, bool Loop, SheetClipFrame[] Frames);

/// <summary>One validated sprite sheet: the texture it cuts from, its sockets, frames and clips.</summary>
/// <param name="TextureKey">The texture's key.</param>
/// <param name="TextureExtension">The extension the sheet spelled the texture with.</param>
internal readonly record struct SpriteSheet(
    string TextureKey,
    string TextureExtension,
    string[] Sockets,
    SheetFrame[] Frames,
    SheetClip[] Clips);

/// <summary>
/// Reads the sprite sheet document format. The sheet is known at build time. The generated registry
/// carries it as literal data, and no document ships or is parsed at run time.
/// </summary>
internal static class SpriteSheetFile
{
    private const int SupportedFormat = 1;

    /// <summary>Reads and validates the sheet at <paramref name="path"/>.</summary>
    /// <exception cref="FormatException">The JSON is malformed or the document breaks the format.</exception>
    internal static SpriteSheet Read(string path)
    {
        SheetJson? json;
        try
        {
            using FileStream stream = File.OpenRead(path);
            json = JsonSerializer.Deserialize(stream, SheetJsonContext.Default.SheetJson);
        }
        catch (JsonException ex)
        {
            throw new FormatException(ex.Message, ex);
        }

        return json is null
            ? throw new FormatException("is empty. A sheet document is one JSON object.")
            : Validate(json);
    }

    private static SpriteSheet Validate(SheetJson json)
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
        string[] sockets = Sockets(json.Sockets);
        SheetFrame[] frames = Frames(json.Frames, sockets);

        return new SpriteSheet(key, extension, sockets, frames, Clips(json.Clips, frames));
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

    private static string[] Sockets(List<SocketJson>? declared)
    {
        if (declared is not { Count: > 0 })
        {
            return [];
        }

        // Sockets have their own name space. A socket may share a name with a frame or a clip.
        Names named = new(SpriteStep.SocketsClass);
        string[] sockets = new string[declared.Count];

        for (int i = 0; i < declared.Count; i++)
        {
            sockets[i] = named.Read(declared[i]?.Name, $"sockets[{i}]");
        }

        return sockets;
    }

    private static SheetFrame[] Frames(List<FrameJson>? declared, string[] sockets)
    {
        if (declared is null)
        {
            throw new FormatException("has no frames. A sheet names at least one region of its texture.");
        }

        if (declared.Count == 0)
        {
            throw new FormatException("has an empty frames list. A sheet names at least one region of its texture.");
        }

        SheetFrame[] frames = new SheetFrame[declared.Count];
        Names named = new(SpriteStep.FramesClass);
        bool[] set = new bool[sockets.Length];

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

            (float pivotX, float pivotY) = Pivot(frame.Pivot, name);
            frames[i] = new SheetFrame(name, x, y, width, height, pivotX, pivotY, FrameSockets(frame, name, sockets, set));
        }

        for (int i = 0; i < sockets.Length; i++)
        {
            if (!set[i])
            {
                throw new FormatException(
                    $"declares socket \"{sockets[i]}\", which no frame sets. A socket is a point on the frames that carry it, so either a frame sets it or the declaration goes.");
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

            if (point.Length != 2)
            {
                throw new FormatException(
                    $"has frame \"{name}\" setting socket \"{declared[i]}\" with {point.Length} components. A socket is written [x, y] in texels of the frame from its top-left corner, as a pivot is.");
            }

            // A JSON number too large for a float reads as an infinity, which is no texel offset.
            if (!float.IsFinite(point[0]) || !float.IsFinite(point[1]))
            {
                throw new FormatException(
                    $"has frame \"{name}\" setting socket \"{declared[i]}\" to a point that is not finite. A socket is a pair of texel offsets.");
            }

            set[i] = true;
            sockets.Add(new SheetSocket(declared[i], point[0], point[1]));
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

    private static SheetClip[] Clips(List<ClipJson>? declared, SheetFrame[] frames)
    {
        // A sheet may declare frames only. A static sprite is one frame drawn with no animator.
        if (declared is not { Count: > 0 })
        {
            return [];
        }

        HashSet<string> frameNames = new(StringComparer.Ordinal);
        foreach (SheetFrame frame in frames)
        {
            frameNames.Add(frame.Name);
        }

        SheetClip[] clips = new SheetClip[declared.Count];

        // Frames and clips are separate name spaces. A frame and a clip may share a name.
        Names named = new(SpriteStep.ClipsClass);

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

                sequence[j] = new SheetClipFrame(frame, ticks);
            }

            clips[i] = new SheetClip(name, clip.Loop ?? false, sequence);
        }

        return clips;
    }

    private static string Missing(FrameJson frame) =>
        frame.X is null ? "x" : frame.Y is null ? "y" : frame.Width is null ? "width" : "height";

    private static (float X, float Y) Pivot(float[]? pivot, string name)
    {
        if (pivot is null)
        {
            return (0F, 0F);
        }

        if (pivot.Length != 2)
        {
            throw new FormatException(
                $"has frame \"{name}\" with a pivot of {pivot.Length} components. A pivot is written [x, y] in texels of the frame from its top-left corner, and a frame anchored at that corner leaves it out.");
        }

        return !float.IsFinite(pivot[0]) || !float.IsFinite(pivot[1])
            ? throw new FormatException(
                $"has frame \"{name}\" with a pivot that is not finite. A pivot is a pair of texel offsets.")
            : (pivot[0], pivot[1]);
    }

    // One rule for all three name spaces: non-empty, unique, an identifier, and not the name of the
    // generated class the member is declared on, which C# refuses (CS0542).
    private sealed class Names(string reserved)
    {
        private readonly HashSet<string> _byName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _byIdentifier = new(StringComparer.Ordinal);

        internal string Read(string? authored, string position)
        {
            if (authored is not { Length: > 0 } name)
            {
                throw new FormatException($"has {position} with no name. Every frame, clip and socket is named.");
            }

            if (!_byName.Add(name))
            {
                throw new FormatException(
                    $"has {position} as a second \"{name}\". Names are unique within their list, since a game reaches each by name.");
            }

            if (TypeNaming.ToIdentifier(name) is not { } identifier)
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
