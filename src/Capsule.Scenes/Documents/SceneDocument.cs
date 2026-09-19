using System.Globalization;
using System.Numerics;

namespace Capsule.Scenes.Documents;

/// <summary>
/// A scene as data, held as one ordered list of engine-native tile maps and game-defined entity
/// placements. File order is composition order. The constructor enforces the format's invariants, so
/// every document that exists is valid.
/// </summary>
public sealed class SceneDocument
{
    // The entry type the engine reserves for tile maps. A document may hold any number of them.
    internal const string TileMapType = "tile-map";

    private const int Sha256HexLength = 64;

    private readonly SceneDocumentEntry[] _entries;

    /// <param name="entries">Every tile map and entity placement, in composition order.</param>
    /// <param name="nextEntityId">The next id to hand out. At least 1, and greater than every entry's id.</param>
    /// <param name="source">Where a derived document came from, or null when it is hand-authored.</param>
    /// <param name="scrollOrigin">The authored <see cref="Camera.ScrollOrigin"/>, or null when the document authors none.</param>
    /// <exception cref="ArgumentException">The document is malformed. The message names the defect.</exception>
    public SceneDocument(
        IReadOnlyList<SceneDocumentEntry> entries,
        int nextEntityId,
        SceneDocumentSource? source = null,
        Vector2? scrollOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        NextEntityId = nextEntityId;
        Source = source;
        ScrollOrigin = scrollOrigin;
        _entries = [.. entries];

        Validate();
    }

    /// <summary>Every tile map and entity placement, in composition order.</summary>
    public ReadOnlySpan<SceneDocumentEntry> Entries => _entries;

    /// <summary>
    /// The next id to hand out. It rises and never falls, ids are never reused, and deleting an entry
    /// does not rewind it. Every entry's id is below this value.
    /// </summary>
    public int NextEntityId { get; }

    /// <summary>The authoring source this document was derived from, or null when it is hand-authored.</summary>
    public SceneDocumentSource? Source { get; }

    /// <summary>
    /// The <see cref="Camera.ScrollOrigin"/> written to every camera installed in the composed scene, or
    /// null when the document authors none and each camera keeps its own.
    /// </summary>
    public Vector2? ScrollOrigin { get; }

    private void Validate()
    {
        if (NextEntityId < 1)
        {
            throw Malformed($"nextEntityId is {NextEntityId}. Set it to at least 1.", nameof(NextEntityId));
        }

        if (ScrollOrigin is { } origin && (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y)))
        {
            throw Malformed(string.Create(
                CultureInfo.InvariantCulture,
                $"scrollOrigin is ({origin.X}, {origin.Y}), which is not a position. Make both components finite."),
                nameof(ScrollOrigin));
        }

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
                throw Malformed($"{identity} has no id; assign one from nextEntityId when the entry is created.");
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
