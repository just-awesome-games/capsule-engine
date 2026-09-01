using System.Globalization;

namespace Capsule.Scenes.Documents;

/// <summary>
/// A scene as data: one ordered list of engine-native tile maps and game-defined entity
/// placements. File order is composition order.
/// </summary>
public sealed class SceneDocument
{
    /// <summary>
    /// The entry type the engine reserves for tile maps. Any number may appear; each composes at
    /// its position in the document's entry list.
    /// </summary>
    internal const string TileMapType = "tile-map";

    private const int Sha256HexLength = 64;

    private readonly SceneDocumentEntry[] _entries;

    /// <param name="entries">Every tile map and entity placement, in composition order.</param>
    /// <param name="nextEntityId">The next id to hand out; at least 1 and above every entry's id.</param>
    /// <param name="source">Provenance when the document is derived, null when it is authored.</param>
    /// <exception cref="SceneDocumentFormatException">Some invariant of the document format is broken.</exception>
    public SceneDocument(
        IReadOnlyList<SceneDocumentEntry> entries,
        int nextEntityId,
        SceneDocumentSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        NextEntityId = nextEntityId;
        Source = source;
        _entries = [.. entries];

        Validate();
    }

    /// <summary>Every tile map and entity placement, in composition order.</summary>
    public ReadOnlySpan<SceneDocumentEntry> Entries => _entries;

    /// <summary>
    /// The next id to hand out. Monotonic: ids are never reused, and deleting an entry never
    /// rewinds it. Every entry's id is below it.
    /// </summary>
    public int NextEntityId { get; }

    /// <summary>The authoring source this document was derived from, or null when it is hand-authored.</summary>
    public SceneDocumentSource? Source { get; }

    private void Validate()
    {
        if (NextEntityId < 1)
        {
            throw Malformed($"nextEntityId must be at least 1, not {NextEntityId}.");
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
            if (entity is null && tileMap is null)
            {
                throw Malformed($"entries[{i}] has no entry type.");
            }

            // Identity is minted where the document is authored — by the authoring tool, or from
            // nextEntityId in the code that builds one — never by the reader.
            if (entry.Id < 1)
            {
                string identity = entity is { } unidentified
                    ? string.Create(CultureInfo.InvariantCulture, $"entity '{unidentified.Type}' at ({unidentified.X}, {unidentified.Y})")
                    : $"the '{TileMapType}' entry";
                throw Malformed($"{identity} has no id — every entry takes one from nextEntityId when it is created.");
            }

            if (tileMap is { Grid: null })
            {
                throw Malformed($"the '{TileMapType}' entry carries no grid; its properties are the grid it draws.");
            }

            if (entity is { } placed
                && string.Equals(placed.Type, TileMapType, StringComparison.Ordinal))
            {
                throw Malformed(
                    $"the type '{TileMapType}' is reserved for {nameof(TileMapPlacement)} entries.");
            }

            if (entity is { } placedWithoutType && string.IsNullOrWhiteSpace(placedWithoutType.Type))
            {
                throw Malformed($"entity id {placedWithoutType.Id} has no type.");
            }

            // NaN and the infinities have no JSON number, so one of them here would construct a
            // document that cannot be written back out.
            if (!float.IsFinite(entry.X) || !float.IsFinite(entry.Y))
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"entity id {entry.Id} is at ({entry.X}, {entry.Y}), which is not a position."));
            }

            if (entry.Id >= NextEntityId)
            {
                throw Malformed($"entity id {entry.Id} is not below nextEntityId {NextEntityId}.");
            }

            if (!seen.Add(entry.Id))
            {
                throw Malformed($"entity id {entry.Id} appears more than once.");
            }
        }
    }

    // A half-filled block writes a source object that Parse then rejects, so the document would not
    // survive its own round trip. The path and hash shapes are enforced here too: a document whose
    // source block is unresolvable on another machine, or carries a hash no importer could have
    // produced, records provenance that says nothing.
    private void ValidateSource()
    {
        if (Source is not { } source)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(source.Tool) || string.IsNullOrWhiteSpace(source.Path)
            || string.IsNullOrWhiteSpace(source.Hash))
        {
            throw Malformed("source must carry a tool, a path and a hash.");
        }

        if (!IsPortableRelativePath(source.Path))
        {
            throw Malformed(
                $"source.path '{source.Path}' must be relative and use forward slashes.");
        }

        if (!IsSha256Hex(source.Hash))
        {
            throw Malformed($"source.hash must be 64 lowercase hex characters, not '{source.Hash}'.");
        }
    }

    // Deliberately not Path.IsPathRooted: what counts as rooted differs between Windows and
    // Linux, and a scene document must mean the same thing on both.
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

    private static SceneDocumentFormatException Malformed(string message) => new(message);
}
