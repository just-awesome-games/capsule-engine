using System.Globalization;
using Capsule.Scenes.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>
/// A scene as data: one uniform list of entries, each with an id, a type and a position. The
/// engine claims one type of its own, <see cref="TileMapType"/>, whose properties carry the
/// terrain grid; every other entry is a placed entity the game's own class spawns.
/// </summary>
public sealed class SceneDocument
{
    /// <summary>
    /// The entry type the engine reserves for a scene's terrain. At most one entry may carry it,
    /// and it is the document's first entry, so terrain composes under everything placed on it.
    /// </summary>
    public const string TileMapType = "tile-map";

    private const int Sha256HexLength = 64;

    private readonly EntityPlacement[] _entities;

    /// <param name="tileMap">The terrain entry, or null for a scene of entities alone.</param>
    /// <param name="entities">Every other entry, in file order.</param>
    /// <param name="nextEntityId">The next id to hand out; at least 1 and above every entry's id.</param>
    /// <param name="source">Provenance when the document is derived, null when it is authored.</param>
    /// <exception cref="SceneDocumentFormatException">Some invariant of the document format is broken.</exception>
    public SceneDocument(
        TileMapPlacement? tileMap,
        IReadOnlyList<EntityPlacement> entities,
        int nextEntityId,
        SceneDocumentSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(entities);

        TileMap = tileMap;
        NextEntityId = nextEntityId;
        Source = source;
        _entities = [.. entities];

        Validate();
    }

    /// <summary>The terrain entry, or null when the document carries none.</summary>
    public TileMapPlacement? TileMap { get; }

    /// <summary>The terrain's grid, or null when the document carries no terrain entry.</summary>
    public TileGrid? Grid => TileMap?.Grid;

    /// <summary>
    /// The next id to hand out. Monotonic: ids are never reused, and deleting an entry never
    /// rewinds it. Every entry's id is below it, the terrain's included.
    /// </summary>
    public int NextEntityId { get; }

    /// <summary>The authoring source this document was derived from, or null when it is hand-authored.</summary>
    public SceneDocumentSource? Source { get; }

    /// <summary>Every entry but the terrain, in file order.</summary>
    public ReadOnlySpan<EntityPlacement> Entities => _entities;

    private void Validate()
    {
        if (NextEntityId < 1)
        {
            throw Malformed($"nextEntityId must be at least 1, not {NextEntityId}.");
        }

        HashSet<int> seen = [];
        ValidateTileMap(seen);
        ValidateEntities(seen);
        ValidateSource();
    }

    private void ValidateTileMap(HashSet<int> seen)
    {
        if (TileMap is not { } tileMap)
        {
            return;
        }

        if (tileMap.Grid is null)
        {
            throw Malformed($"the '{TileMapType}' entry carries no grid; its properties are the grid it draws.");
        }

        if (tileMap.Id < 1)
        {
            throw Malformed(
                $"the '{TileMapType}' entry has no id — every entry takes one from nextEntityId when it is created.");
        }

        if (tileMap.Id >= NextEntityId)
        {
            throw Malformed($"entity id {tileMap.Id} is not below nextEntityId {NextEntityId}.");
        }

        seen.Add(tileMap.Id);
    }

    private void ValidateEntities(HashSet<int> seen)
    {
        for (int i = 0; i < _entities.Length; i++)
        {
            EntityPlacement placed = _entities[i];

            if (string.Equals(placed.Type, TileMapType, StringComparison.Ordinal))
            {
                throw Malformed(
                    $"a '{TileMapType}' entry must be the document's first entry, and a document carries at most one.");
            }

            // Identity is minted where the document is authored — by the authoring tool, or from
            // nextEntityId in the code that builds one — never by the reader.
            if (placed.Id < 1)
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"entity '{placed.Type}' at ({placed.X}, {placed.Y}) has no id — every entry takes one from nextEntityId when it is created."));
            }

            if (string.IsNullOrWhiteSpace(placed.Type))
            {
                throw Malformed($"entity id {placed.Id} has no type.");
            }

            // NaN and the infinities have no JSON number, so one of them here would construct a
            // document that cannot be written back out.
            if (!float.IsFinite(placed.X) || !float.IsFinite(placed.Y))
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"entity id {placed.Id} is at ({placed.X}, {placed.Y}), which is not a position."));
            }

            if (placed.Id >= NextEntityId)
            {
                throw Malformed($"entity id {placed.Id} is not below nextEntityId {NextEntityId}.");
            }

            if (!seen.Add(placed.Id))
            {
                throw Malformed($"entity id {placed.Id} appears more than once.");
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
