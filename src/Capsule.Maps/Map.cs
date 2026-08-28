using System.Globalization;

namespace Capsule.Maps;

/// <summary>A validated tile grid and its placed objects.</summary>
public sealed class Map
{
    private const int Sha256HexLength = 64;

    private readonly MapObject[] _objects;

    /// <exception cref="MapFormatException">Some invariant of the map format is broken.</exception>
    public Map(
        TileGrid grid,
        IReadOnlyList<MapObject> objects,
        int nextObjectId,
        MapSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(objects);

        Grid = grid;
        NextObjectId = nextObjectId;
        Source = source;
        _objects = [.. objects];

        Validate();
    }

    /// <summary>The terrain the objects are placed over.</summary>
    public TileGrid Grid { get; }

    /// <summary>
    /// The next id to hand out. Monotonic: ids are never reused, and deleting an object never
    /// rewinds it. Every object id is below it.
    /// </summary>
    public int NextObjectId { get; }

    /// <summary>The authoring source this map was generated from, or null when it is hand-authored.</summary>
    public MapSource? Source { get; }

    /// <summary>The placed objects, in file order.</summary>
    public ReadOnlySpan<MapObject> Objects => _objects;

    private void Validate()
    {
        if (NextObjectId < 1)
        {
            throw Malformed($"nextObjectId must be at least 1, not {NextObjectId}.");
        }

        ValidateObjects();
        ValidateSource();
    }

    private void ValidateObjects()
    {
        HashSet<int> seen = [];
        for (int i = 0; i < _objects.Length; i++)
        {
            MapObject placed = _objects[i];

            // Identity is minted where the map is authored — by the authoring tool, or from
            // nextObjectId in the code that builds one — never by the reader.
            if (placed.Id < 1)
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"object '{placed.Type}' at ({placed.X}, {placed.Y}) has no id — every object takes one from nextObjectId when it is created."));
            }

            if (string.IsNullOrWhiteSpace(placed.Type))
            {
                throw Malformed($"object id {placed.Id} has no type.");
            }

            // NaN and the infinities have no JSON number, so one of them here would construct
            // a map that cannot be written back out.
            if (!float.IsFinite(placed.X) || !float.IsFinite(placed.Y))
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"object id {placed.Id} is at ({placed.X}, {placed.Y}), which is not a position."));
            }

            if (placed.Id >= NextObjectId)
            {
                throw Malformed($"object id {placed.Id} is not below nextObjectId {NextObjectId}.");
            }

            if (!seen.Add(placed.Id))
            {
                throw Malformed($"object id {placed.Id} appears more than once.");
            }
        }
    }

    // A half-filled block writes a source object that Parse then rejects, so the map would not
    // survive its own round trip. The path and hash shapes are enforced here too: a map whose
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
    // Linux, and a map file must mean the same thing on both.
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

    private static MapFormatException Malformed(string message) => new(message);
}
