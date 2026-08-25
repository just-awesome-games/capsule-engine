using System.Globalization;

namespace Capsule.Levels;

/// <summary>
/// A tile grid plus the entities placed on it. Always valid: the constructor rejects anything
/// the format forbids, so a level that exists is a level a game can trust. Constructing one
/// from code is a first-class path — a file is one way to get a level, never the only one.
/// </summary>
public sealed class Level
{
    /// <summary>The palette entry at index 0, meaning "no tile here". Reserved; never a game's tile type.</summary>
    public const string EmptyTileType = "empty";

    private const int Sha256HexLength = 64;

    private readonly string[] _tileTypes;
    private readonly int[] _tiles;
    private readonly LevelEntity[] _entities;

    /// <exception cref="LevelFormatException">Some invariant of the level format is broken.</exception>
    public Level(
        int tileSize,
        int width,
        int height,
        IReadOnlyList<string> tileTypes,
        IReadOnlyList<int> tiles,
        IReadOnlyList<LevelEntity> entities,
        int nextEntityId,
        LevelSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(tileTypes);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(entities);

        TileSize = tileSize;
        Width = width;
        Height = height;
        NextEntityId = nextEntityId;
        Source = source;
        _tileTypes = [.. tileTypes];
        _tiles = [.. tiles];
        _entities = [.. entities];

        Validate();
    }

    /// <summary>The edge length of one tile in pixels. Supplied by the level; the engine has no opinion.</summary>
    public int TileSize { get; }

    /// <summary>Grid width in tiles.</summary>
    public int Width { get; }

    /// <summary>Grid height in tiles.</summary>
    public int Height { get; }

    /// <summary>
    /// The next id to hand out. Monotonic: ids are never reused, and deleting an entity never
    /// rewinds it. Every entity id is below it.
    /// </summary>
    public int NextEntityId { get; }

    /// <summary>The authoring source this level was generated from, or null when it is hand-authored.</summary>
    public LevelSource? Source { get; }

    /// <summary>The tile palette. Index 0 is <see cref="EmptyTileType"/>; names are unique.</summary>
    public ReadOnlySpan<string> TileTypes => _tileTypes;

    /// <summary>Palette indices, row-major, <see cref="Width"/> * <see cref="Height"/> of them.</summary>
    public ReadOnlySpan<int> Tiles => _tiles;

    /// <summary>The placed entities, in file order.</summary>
    public ReadOnlySpan<LevelEntity> Entities => _entities;

    /// <summary>The palette index at a tile coordinate; 0 where the grid is empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public int TileAt(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        return _tiles[(y * Width) + x];
    }

    /// <summary>The tile type name at a tile coordinate; <see cref="EmptyTileType"/> where the grid is empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate is off the grid.</exception>
    public string TileTypeAt(int x, int y) => _tileTypes[TileAt(x, y)];

    private void Validate()
    {
        if (TileSize <= 0)
        {
            throw Malformed($"tileSize must be positive, not {TileSize}.");
        }

        if (Width <= 0)
        {
            throw Malformed($"width must be positive, not {Width}.");
        }

        if (Height <= 0)
        {
            throw Malformed($"height must be positive, not {Height}.");
        }

        ValidatePalette();
        ValidateTiles();

        if (NextEntityId < 1)
        {
            throw Malformed($"nextEntityId must be at least 1, not {NextEntityId}.");
        }

        ValidateEntities();
        ValidateSource();
    }

    private void ValidatePalette()
    {
        if (_tileTypes.Length == 0 || _tileTypes[0] != EmptyTileType)
        {
            string actual = _tileTypes.Length == 0 ? "an empty palette" : $"\"{_tileTypes[0]}\"";
            throw Malformed($"tileTypes[0] must be \"{EmptyTileType}\", not {actual}.");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < _tileTypes.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(_tileTypes[i]))
            {
                throw Malformed($"tileTypes[{i}] is blank; every tile type must be named.");
            }

            if (!seen.Add(_tileTypes[i]))
            {
                throw Malformed($"tileTypes[{i}] repeats \"{_tileTypes[i]}\"; tile type names must be unique.");
            }
        }
    }

    private void ValidateTiles()
    {
        // Widened deliberately: an int product wraps, and 65536 x 65536 wrapping to 0 would
        // let an empty tiles array pass here and fail much later inside TileAt.
        long expected = (long)Width * Height;
        if (_tiles.Length != expected)
        {
            throw Malformed(
                $"tiles has {_tiles.Length} entries but width {Width} x height {Height} requires {expected}.");
        }

        for (int i = 0; i < _tiles.Length; i++)
        {
            if (_tiles[i] < 0 || _tiles[i] >= _tileTypes.Length)
            {
                throw Malformed(
                    $"tiles[{i}] is {_tiles[i]}, which is not a tileTypes index (0..{_tileTypes.Length - 1}).");
            }
        }
    }

    private void ValidateEntities()
    {
        HashSet<int> seen = [];
        for (int i = 0; i < _entities.Length; i++)
        {
            LevelEntity entity = _entities[i];

            // Identity is minted where the level is authored — by the authoring tool, or from
            // nextEntityId in the code that builds one — never by the reader.
            if (entity.Id < 1)
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"entity '{entity.Type}' at ({entity.X}, {entity.Y}) has no id — every entity takes one from nextEntityId when it is created."));
            }

            if (string.IsNullOrWhiteSpace(entity.Type))
            {
                throw Malformed($"entity id {entity.Id} has no type.");
            }

            // NaN and the infinities have no JSON number, so one of them here would construct
            // a level that cannot be written back out.
            if (!float.IsFinite(entity.X) || !float.IsFinite(entity.Y))
            {
                throw Malformed(string.Create(
                    CultureInfo.InvariantCulture,
                    $"entity id {entity.Id} is at ({entity.X}, {entity.Y}), which is not a position."));
            }

            if (entity.Id >= NextEntityId)
            {
                throw Malformed($"entity id {entity.Id} is not below nextEntityId {NextEntityId}.");
            }

            if (!seen.Add(entity.Id))
            {
                throw Malformed($"entity id {entity.Id} appears more than once.");
            }
        }
    }

    // A half-filled block writes a source object that Parse then rejects, so the level would
    // not survive its own round trip. The path and hash shapes are enforced here too: a level
    // whose source block is unresolvable on another machine, or carries a hash no importer
    // could have produced, records provenance that says nothing.
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
    // Linux, and a level file must mean the same thing on both.
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

    private static LevelFormatException Malformed(string message) => new(message);
}
