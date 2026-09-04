using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Build.Sprites;

/// <summary>
/// Reading and writing the sprite sheet document format. The written form is canonical — fixed
/// field order, two-space indent, LF, UTF-8 without a BOM, one trailing newline — so re-generating
/// an unchanged document reproduces its bytes exactly and a diff shows only real change.
/// </summary>
public static class SpriteSheetDocumentFile
{
    /// <summary>The extension a sheet document is authored under, both halves of it.</summary>
    public const string DocumentExtension = ".sheet.json";

    private const int FormatVersion = 1;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Reads and validates the sheet document at <paramref name="path"/>.</summary>
    /// <exception cref="SpriteSheetFormatException">The file is malformed; the message is prefixed with the path.</exception>
    public static SpriteSheetDocument Load(string path)
    {
        string json = Text(File.ReadAllBytes(path));

        try
        {
            return Parse(json);
        }
        catch (SpriteSheetFormatException ex)
        {
            throw new SpriteSheetFormatException($"{path}: {ex.Message}", ex);
        }
    }

    /// <summary>Reads and validates sheet document JSON that is already in hand.</summary>
    /// <exception cref="SpriteSheetFormatException">The JSON is malformed or the document breaks the format.</exception>
    public static SpriteSheetDocument Parse(string json)
    {
        SpriteSheetJson file = Deserialize(json);

        if (file.FormatVersion is not { } formatVersion)
        {
            throw new SpriteSheetFormatException(
                $"the sheet document has no formatVersion; this build supports formatVersion {FormatVersion}.");
        }

        if (formatVersion != FormatVersion)
        {
            throw new SpriteSheetFormatException(
                $"formatVersion {formatVersion} is unsupported; this build supports formatVersion {FormatVersion}.");
        }

        TextureHandle texture = Texture(file.Texture);
        SpriteSheetFrame[] frames = Frames(file.Frames);
        SpriteSheetClip[] clips = Clips(file.Clips, frames);

        return new SpriteSheetDocument(texture, frames, clips, ToSource(file.Source));
    }

    /// <summary>The canonical text of <paramref name="document"/>.</summary>
    public static string ToJson(SpriteSheetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        SpriteSheetFrameJson[] frames = new SpriteSheetFrameJson[document.Frames.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            SpriteSheetFrame frame = document.Frames[i];
            frames[i] = new SpriteSheetFrameJson
            {
                Name = frame.Name,
                X = frame.Region.X,
                Y = frame.Region.Y,
                Width = frame.Region.Width,
                Height = frame.Region.Height,

                // Written only where it says something: the top-left corner is what an absent pivot
                // means, so emitting it would put a field on every untrimmed frame.
                Pivot = frame.Pivot == Vector2.Zero ? null : [frame.Pivot.X, frame.Pivot.Y],
            };
        }

        SpriteSheetClipJson[] clips = new SpriteSheetClipJson[document.Clips.Count];
        for (int i = 0; i < clips.Length; i++)
        {
            SpriteSheetClip clip = document.Clips[i];
            SpriteSheetClipFrameJson[] entries = new SpriteSheetClipFrameJson[clip.Frames.Count];
            for (int j = 0; j < entries.Length; j++)
            {
                entries[j] = new SpriteSheetClipFrameJson { Frame = clip.Frames[j].Frame, Ticks = clip.Frames[j].Ticks };
            }

            clips[i] = new SpriteSheetClipJson
            {
                Name = clip.Name,

                // Absent is false, which is most clips.
                Loop = clip.Loop ? true : null,
                Frames = entries,
            };
        }

        SpriteSheetJson file = new()
        {
            FormatVersion = FormatVersion,
            Texture = TextureName(document.Texture),
            Frames = frames,
            Clips = clips,
            Source = document.Source is { } source
                ? new SpriteSheetSourceJson { Tool = source.Tool, Path = source.Path, Hash = source.Hash }
                : null,
        };

        string json = JsonSerializer.Serialize(file, SpriteSheetJsonContext.Default.SpriteSheetJson);

        // The writer's newline is platform-dependent; the format's is not.
        return json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    /// <summary>Writes <paramref name="document"/> to <paramref name="path"/> in canonical form.</summary>
    public static void Save(SpriteSheetDocument document, string path) =>
        File.WriteAllText(path, ToJson(document), Utf8NoBom);

    private static SpriteSheetFrame[] Frames(SpriteSheetFrameJson?[]? authored)
    {
        if (authored is not { } entries)
        {
            throw new SpriteSheetFormatException(
                "the sheet document has no frames; a sheet names at least one region of its texture.");
        }

        if (entries.Length == 0)
        {
            throw new SpriteSheetFormatException(
                "the sheet document has an empty frames list; a sheet names at least one region of its texture.");
        }

        SpriteSheetFrame[] frames = new SpriteSheetFrame[entries.Length];
        HashSet<string> byName = new(entries.Length, StringComparer.Ordinal);
        Dictionary<string, string> byIdentifier = new(entries.Length, StringComparer.Ordinal);

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] is not { } entry)
            {
                throw new SpriteSheetFormatException(
                    $"frames[{i}] is null; every frame is an object with a name and a region.");
            }

            string name = Name(entry.Name, $"frames[{i}]", byName, byIdentifier, SpriteRegistrySource.FramesClass);

            if (entry.X is not { } x || entry.Y is not { } y || entry.Width is not { } width || entry.Height is not { } height)
            {
                throw new SpriteSheetFormatException(
                    $"frame \"{name}\" has no {Missing(entry)}; every frame carries x, y, width and height in texels.");
            }

            if (x < 0 || y < 0)
            {
                throw new SpriteSheetFormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"frame \"{name}\" is at ({x}, {y}); a region starts inside its texture, so x and y are not negative."));
            }

            if (width <= 0 || height <= 0)
            {
                throw new SpriteSheetFormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"frame \"{name}\" is {width}x{height}; a region has at least one texel on each axis."));
            }

            frames[i] = new SpriteSheetFrame(name, new TextureRegion(x, y, width, height), Pivot(entry.Pivot, name));
        }

        return frames;
    }

    private static SpriteSheetClip[] Clips(SpriteSheetClipJson?[]? authored, SpriteSheetFrame[] frames)
    {
        if (authored is not { } entries)
        {
            throw new SpriteSheetFormatException(
                "the sheet document has no clips; a sheet plays at least one clip over its frames.");
        }

        if (entries.Length == 0)
        {
            throw new SpriteSheetFormatException(
                "the sheet document has an empty clips list; a sheet plays at least one clip over its frames.");
        }

        HashSet<string> frameNames = new(StringComparer.Ordinal);
        foreach (SpriteSheetFrame frame in frames)
        {
            frameNames.Add(frame.Name);
        }

        SpriteSheetClip[] clips = new SpriteSheetClip[entries.Length];
        HashSet<string> byName = new(entries.Length, StringComparer.Ordinal);
        Dictionary<string, string> byIdentifier = new(entries.Length, StringComparer.Ordinal);

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] is not { } entry)
            {
                throw new SpriteSheetFormatException(
                    $"clips[{i}] is null; every clip is an object with a name and a frame list.");
            }

            // Frames and clips are separate namespaces, so a frame and a clip may share a name.
            string name = Name(entry.Name, $"clips[{i}]", byName, byIdentifier, SpriteRegistrySource.ClipsClass);

            if (entry.Frames is not { Length: > 0 } clipFrames)
            {
                throw new SpriteSheetFormatException(
                    $"clip \"{name}\" has no frames; a clip plays at least one.");
            }

            SpriteSheetClipFrame[] played = new SpriteSheetClipFrame[clipFrames.Length];
            for (int j = 0; j < clipFrames.Length; j++)
            {
                if (clipFrames[j] is not { } entryFrame)
                {
                    throw new SpriteSheetFormatException(
                        $"clip \"{name}\" frame {j} is null; every entry names a frame and how many ticks it is held for.");
                }

                if (entryFrame.Frame is not { Length: > 0 } frame || !frameNames.Contains(frame))
                {
                    throw new SpriteSheetFormatException(
                        $"clip \"{name}\" frame {j} plays \"{entryFrame.Frame}\", which this sheet has no frame named; a clip plays frames of its own sheet.");
                }

                if (entryFrame.Ticks is not { } ticks)
                {
                    throw new SpriteSheetFormatException(
                        $"clip \"{name}\" frame {j} has no ticks; every entry states how many fixed steps its frame is held for.");
                }

                if (ticks <= 0)
                {
                    throw new SpriteSheetFormatException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"clip \"{name}\" frame {j} is held for {ticks} ticks; a frame is held for at least one fixed step, and durations are ticks of the game's fixed step rather than milliseconds."));
                }

                played[j] = new SpriteSheetClipFrame(frame, ticks);
            }

            clips[i] = new SpriteSheetClip(name, entry.Loop ?? false, played);
        }

        return clips;
    }

    // One rule for both name spaces: non-empty, unique, an identifier, and not the name of the
    // generated class the member is declared on — a member may not carry that (CS0542).
    private static string Name(
        string? authored,
        string position,
        HashSet<string> byName,
        Dictionary<string, string> byIdentifier,
        string reserved)
    {
        if (authored is not { Length: > 0 } name)
        {
            throw new SpriteSheetFormatException($"{position} has no name; every frame and clip is named.");
        }

        if (!byName.Add(name))
        {
            throw new SpriteSheetFormatException(
                $"{position} is a second \"{name}\"; names are unique within their list, since a game reaches each by name.");
        }

        if (SpriteSheetNaming.ToIdentifier(name) is not { } identifier)
        {
            throw new SpriteSheetFormatException(
                $"{position} is named \"{name}\", which is no C# name; a name is letters, digits, '-' and '_', and does not start with a digit.");
        }

        if (string.Equals(identifier, reserved, StringComparison.Ordinal))
        {
            throw new SpriteSheetFormatException(
                $"{position} is named \"{name}\", which is the generated '{reserved}' class it would be declared on; name it something else.");
        }

        if (byIdentifier.TryGetValue(identifier, out string? claimed))
        {
            throw new SpriteSheetFormatException(
                $"{position} is named \"{name}\" and \"{claimed}\" is already declared as '{identifier}'; two names that differ only in their separators are one C# name.");
        }

        byIdentifier[identifier] = name;

        return name;
    }

    private static string Missing(SpriteSheetFrameJson entry) =>
        entry.X is null ? "x" : entry.Y is null ? "y" : entry.Width is null ? "width" : "height";

    private static Vector2 Pivot(float[]? pivot, string name)
    {
        if (pivot is null)
        {
            return Vector2.Zero;
        }

        if (pivot.Length != 2)
        {
            throw new SpriteSheetFormatException(
                $"frame \"{name}\" has a pivot of {pivot.Length} components; a pivot is written [x, y] in texels of the frame from its top-left corner, and a frame anchored at that corner leaves it out.");
        }

        if (!float.IsFinite(pivot[0]) || !float.IsFinite(pivot[1]))
        {
            throw new SpriteSheetFormatException(
                $"frame \"{name}\" has a pivot that is not finite; a pivot is a pair of texel offsets.");
        }

        return new Vector2(pivot[0], pivot[1]);
    }

    private static TextureHandle Texture(string? texture)
    {
        if (texture is not { Length: > 0 } path)
        {
            throw new SpriteSheetFormatException(
                "the sheet document names no texture; a sheet cuts its frames from one texture under assets/textures.");
        }

        return AssetPaths.TrySplit(path, out string name, out string extension)
            ? new TextureHandle(name, extension)
            : throw new SpriteSheetFormatException(
                $"the sheet document has texture \"{path}\"; a texture is one asset's path under assets/textures, extension included — \"player.png\" at the root, \"actors/player.png\" below it — with forward slashes and no empty, \".\" or \"..\" segment.");
    }

    private static string TextureName(TextureHandle texture) =>
        AssetPaths.Joins(texture.Name, texture.Extension)
            ? texture.Name + texture.Extension
            : throw new SpriteSheetFormatException(
                $"the sheet document cuts from texture handle (\"{texture.Name}\", \"{texture.Extension}\"), which does not split back out of one texture path: a name is one or more '/'-joined segments, none of them empty, \".\" or \"..\", and an extension is a dot followed by at least one character and no second dot.");

    private static SpriteSheetSource? ToSource(SpriteSheetSourceJson? source) =>
        source is null
            ? null
            : new SpriteSheetSource(source.Tool ?? string.Empty, source.Path ?? string.Empty, source.Hash ?? string.Empty);

    private static SpriteSheetJson Deserialize(string json)
    {
        SpriteSheetJson? document;
        try
        {
            document = JsonSerializer.Deserialize(json, SpriteSheetJsonContext.Default.SpriteSheetJson);
        }
        catch (JsonException ex)
        {
            throw new SpriteSheetFormatException($"malformed sheet document JSON — {ex.Message}", ex);
        }

        return document ?? throw new SpriteSheetFormatException("the sheet document file is empty.");
    }

    // The format is written without one, but an editor may add one, and the JSON reader would find
    // U+FEFF where it expects a brace.
    private static string Text(byte[] utf8)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        ReadOnlySpan<byte> bytes = utf8;

        return Encoding.UTF8.GetString(bytes.StartsWith(bom) ? bytes[bom.Length..] : bytes);
    }
}
