using Capsule.Scenes.Tiles;

namespace Capsule.Scenes.Documents;

/// <summary>
/// One ordered entry in a scene document, represented without boxing either of its two shapes.
/// </summary>
public readonly record struct SceneDocumentEntry
{
    private readonly EntryKind _kind;
    private readonly float _scaleX;
    private readonly float _scaleY;
    private readonly string? _type;
    private readonly TileGrid? _grid;

    private SceneDocumentEntry(EntityPlacement entity)
    {
        _kind = EntryKind.Entity;
        Id = entity.Id;
        X = entity.X;
        Y = entity.Y;
        _scaleX = entity.ScaleX;
        _scaleY = entity.ScaleY;
        _type = entity.Type;
        _grid = null;
    }

    private SceneDocumentEntry(TileMapPlacement tileMap)
    {
        _kind = EntryKind.TileMap;
        Id = tileMap.Id;
        X = 0f;
        Y = 0f;

        // A tile map is anchored and unscaled, so the fields exist only to be handed back
        // unchanged; identity keeps a tile-map entry out of every scale check.
        _scaleX = 1f;
        _scaleY = 1f;
        _type = null;
        _grid = tileMap.Grid;
    }

    /// <summary>The entry's identity in the document's one id space.</summary>
    public int Id { get; }

    /// <summary>The entry's authored world-space X coordinate.</summary>
    public float X { get; }

    /// <summary>The entry's authored world-space Y coordinate.</summary>
    public float Y { get; }

    /// <summary>The game-defined entity placement, or null when this is a tile map.</summary>
    public EntityPlacement? Entity =>
        _kind == EntryKind.Entity ? new EntityPlacement(Id, _type!, X, Y, _scaleX, _scaleY) : null;

    /// <summary>The engine-native tile-map placement, or null when this is a game entity.</summary>
    public TileMapPlacement? TileMap =>
        _kind == EntryKind.TileMap ? new TileMapPlacement(Id, _grid!) : null;

    /// <summary>Wraps a game-defined entity placement as an ordered document entry.</summary>
    public static implicit operator SceneDocumentEntry(EntityPlacement entity) => new(entity);

    /// <summary>Wraps an engine-native tile-map placement as an ordered document entry.</summary>
    public static implicit operator SceneDocumentEntry(TileMapPlacement tileMap) => new(tileMap);

    private enum EntryKind : byte
    {
        Invalid,
        Entity,
        TileMap,
    }
}
