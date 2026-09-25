using System.Globalization;
using Capsule.Assets;
using Capsule.Rendering;

namespace Capsule.Scenes.Documents;

/// <summary>
/// A scene as data, held as one ordered list of engine-native tile maps and game-defined entity
/// placements.
/// </summary>
/// <remarks>
/// File order is composition order. The constructor enforces the format's invariants, and every
/// document that exists is valid.
/// </remarks>
public sealed class SceneDocument
{
    // The entry type the engine reserves for tile maps. A document may hold any number of them.
    internal const string TileMapType = "tile-map";

    private const int Sha256HexLength = 64;

    private const string KeyForm =
        "A key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name.";

    private readonly SceneDocumentEntry[] _entries;

    /// <summary>A validated document over <paramref name="entries"/>.</summary>
    /// <param name="entries">Every tile map and entity placement, in composition order.</param>
    /// <param name="nextEntityId">The next id to hand out. At least 1, and greater than every entry's id.</param>
    /// <param name="source">Where a derived document came from, or null when it is hand-authored.</param>
    /// <param name="settings">The scene-level state the document authors, or null when it authors none.</param>
    /// <exception cref="ArgumentException">The document is malformed. The message names the defect.</exception>
    public SceneDocument(
        IReadOnlyList<SceneDocumentEntry> entries,
        int nextEntityId,
        SceneDocumentSource? source = null,
        SceneSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        NextEntityId = nextEntityId;
        Source = source;
        Settings = settings ?? new SceneSettings();
        _entries = [.. entries];

        Validate();
    }

    /// <summary>Every tile map and entity placement, in composition order.</summary>
    public ReadOnlySpan<SceneDocumentEntry> Entries => _entries;

    /// <summary>
    /// The next id to hand out. It rises and never falls, ids are never reused, and deleting an
    /// entry does not rewind it.
    /// </summary>
    /// <remarks>Every entry's id is below this value.</remarks>
    public int NextEntityId { get; }

    /// <summary>The authoring source this document was derived from, or null when it is hand-authored.</summary>
    public SceneDocumentSource? Source { get; }

    /// <summary>The scene-level state the document authors, never null.</summary>
    public SceneSettings Settings { get; }

    private void Validate()
    {
        if (NextEntityId < 1)
        {
            throw Malformed($"nextEntityId is {NextEntityId}. Set it to at least 1.", nameof(NextEntityId));
        }

        ValidateSettings();
        ValidateEntries();
        ValidateSource();
    }

    private void ValidateEntries()
    {
        HashSet<int> seen = [];
        for (int i = 0; i < _entries.Length; i++)
        {
            SceneDocumentEntry entry = _entries[i];
            EntityPlacement? entity = entry.Entity;
            TileMapPlacement? tileMap = entry.TileMap;

            // The authoring tool mints ids. The reader never assigns one.
            if (entry.Id < 1)
            {
                string identity = entity is { } unidentified
                    ? string.Create(CultureInfo.InvariantCulture, $"entity '{unidentified.Type}' at ({unidentified.X}, {unidentified.Y})")
                    : $"the '{TileMapType}' entry";
                throw Malformed($"{identity} has no id. Assign one from nextEntityId when the entry is created.");
            }

            if (tileMap is { Grid: null })
            {
                throw Malformed($"the '{TileMapType}' entry carries no grid. Write the grid it draws in its properties.");
            }

            if (entity is { } placed && string.Equals(placed.Type, TileMapType, StringComparison.Ordinal))
            {
                throw Malformed($"the type '{TileMapType}' is reserved for {nameof(TileMapPlacement)} entries. Give this entity another type.");
            }

            if (entity is { } placedWithoutType && string.IsNullOrWhiteSpace(placedWithoutType.Type))
            {
                throw Malformed($"entity id {placedWithoutType.Id} has no type. Name the spawn type it composes.");
            }

            // NaN and the infinities have no JSON number, so such a document could not be written out.
            if (!float.IsFinite(entry.X) || !float.IsFinite(entry.Y))
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"entity id {entry.Id} is at ({entry.X}, {entry.Y}), which is not a position. Make both coordinates finite."));
            }

            // A scale of zero or less has no size, and a non-finite one has no JSON number.
            if (entity is { } sized && (!IsScale(sized.ScaleX) || !IsScale(sized.ScaleY)))
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"entity id {entry.Id} is scaled ({sized.ScaleX}, {sized.ScaleY}), which is not a scale. Make both factors finite and greater than zero."));
            }

            if (entry.ScrollFactor is { } factor && (!float.IsFinite(factor.X) || !float.IsFinite(factor.Y)))
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"entity id {entry.Id} has scroll factor ({factor.X}, {factor.Y}), which is not a scroll factor. Make both components finite."));
            }

            // A grid answers queries at its authored cells, but a scrolled grid draws somewhere else.
            if (tileMap is { Grid.Collides: true, ScrollFactor: not null })
            {
                throw Malformed(
                    $"the '{TileMapType}' entry with id {entry.Id} authors a scrollFactor on a palette that collides. Drop the scrollFactor, or remove the layers from the palette.");
            }

            if (entry.Id >= NextEntityId)
            {
                throw Malformed($"entity id {entry.Id} is not below nextEntityId {NextEntityId}. Raise nextEntityId above every id.");
            }

            if (!seen.Add(entry.Id))
            {
                throw Malformed($"entity id {entry.Id} appears more than once. Give every entry a unique id.");
            }
        }
    }

    private void ValidateSettings()
    {
        SceneSettings settings = Settings;

        if (settings.BaseScene is { } baseScene && !AssetPaths.IsKey(baseScene))
        {
            throw Malformed(
                $"baseScene is '{baseScene}', which is not a key. {KeyForm}",
                nameof(Settings));
        }

        if (settings.Camera is { } camera && !AssetPaths.IsKey(camera))
        {
            throw Malformed(
                $"camera is '{camera}', which is not a key. {KeyForm}",
                nameof(Settings));
        }

        if (settings.Size is { } size && (!IsScale(size.X) || !IsScale(size.Y)))
        {
            throw Malformed(string.Create(
                CultureInfo.InvariantCulture,
                $"size is ({size.X}, {size.Y}), which is not a size. Make both components finite and greater than zero."),
                nameof(Settings));
        }

        if (settings.ScrollCenter is { } center && (!float.IsFinite(center.X) || !float.IsFinite(center.Y)))
        {
            throw Malformed(string.Create(
                CultureInfo.InvariantCulture,
                $"scrollCenter is ({center.X}, {center.Y}), which is not a position. Make both components finite."),
                nameof(Settings));
        }

        // The format writes a colour as #rrggbb, which carries no alpha. This is the one opacity check for
        // a document read from a file as well.
        if (settings.ClearColor is { A: not byte.MaxValue } clearColor)
        {
            throw Malformed($"clearColor has alpha {clearColor.A}. A scene document authors an opaque clear colour. Set its alpha to 255.", nameof(Settings));
        }

        if (settings.Ambient is { A: not byte.MaxValue } ambient)
        {
            throw Malformed($"ambient has alpha {ambient.A}. A scene document authors an opaque ambient colour. Set its alpha to 255.", nameof(Settings));
        }

        if (settings.Sampling is { } sampling && !Enum.IsDefined(sampling))
        {
            throw Malformed($"sampling is {(int)sampling}, which is not a {nameof(TextureSampling)}. Use one of its named values.", nameof(Settings));
        }
    }

    // A half-filled source block writes an object the reader would reject, so the document would not
    // survive a round trip. The path and hash shapes are checked here for the same reason.
    private void ValidateSource()
    {
        if (Source is not { } source)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(source.Tool) || string.IsNullOrWhiteSpace(source.Path)
            || string.IsNullOrWhiteSpace(source.Hash))
        {
            throw Malformed("source is incomplete. Give it a tool, a path and a hash.", nameof(Source));
        }

        if (!IsPortableRelativePath(source.Path))
        {
            throw Malformed($"source.path '{source.Path}' must be relative and use forward slashes.", nameof(Source));
        }

        if (!IsSha256Hex(source.Hash))
        {
            throw Malformed($"source.hash is '{source.Hash}'. Write 64 lowercase hex characters.", nameof(Source));
        }
    }

    private static bool IsScale(float factor) => float.IsFinite(factor) && factor > 0f;

    // Does not use Path.IsPathRooted, because what counts as rooted differs between Windows and Linux and
    // a scene document must mean the same thing on both.
    private static bool IsPortableRelativePath(string path) =>
        !path.Contains('\\', StringComparison.Ordinal)
        && !path.StartsWith('/')
        && (path.Length < 2 || path[1] != ':');

    private static bool IsSha256Hex(string hash)
    {
        if (hash.Length != Sha256HexLength)
        {
            return false;
        }

        foreach (char character in hash)
        {
            if (!char.IsAsciiHexDigitLower(character))
            {
                return false;
            }
        }

        return true;
    }

    private static ArgumentException Malformed(string message, string parameterName = "entries") =>
        new(message, parameterName);
}
