using System.Numerics;

namespace Capsule.Scenes.Documents;

/// <summary>One entry in a scene document's ordered list, holding either shape without boxing.</summary>
public readonly record struct SceneDocumentEntry
{
    private readonly EntityPlacement _entity;
    private readonly TileMapPlacement _tileMap;
    private readonly bool _isTileMap;

    private SceneDocumentEntry(EntityPlacement entity) => _entity = entity;

    private SceneDocumentEntry(TileMapPlacement tileMap)
    {
        _tileMap = tileMap;
        _isTileMap = true;
    }

    /// <summary>The entry's id in the document's single id space.</summary>
    public int Id => _isTileMap ? _tileMap.Id : _entity.Id;

    /// <summary>The entry's authored world-space X coordinate, zero for a tile map.</summary>
    public float X => _entity.X;

    /// <summary>The entry's authored world-space Y coordinate, zero for a tile map.</summary>
    public float Y => _entity.Y;

    /// <summary>The entry's authored draw band, or null when it authors none.</summary>
    public int? ZIndex => _isTileMap ? _tileMap.ZIndex : _entity.ZIndex;

    /// <summary>The entry's authored scroll factor, or null when it authors none.</summary>
    public Vector2? ScrollFactor => _isTileMap ? _tileMap.ScrollFactor : _entity.ScrollFactor;

    /// <summary>The game-defined entity placement, or null when this is a tile map.</summary>
    public EntityPlacement? Entity => _isTileMap ? null : _entity;

    /// <summary>The engine-native tile-map placement, or null when this is a game entity.</summary>
    public TileMapPlacement? TileMap => _isTileMap ? _tileMap : null;

    /// <summary>Wraps a game-defined entity placement as an ordered document entry.</summary>
    public static implicit operator SceneDocumentEntry(EntityPlacement entity) => new(entity);

    /// <summary>Wraps an engine-native tile-map placement as an ordered document entry.</summary>
    public static implicit operator SceneDocumentEntry(TileMapPlacement tileMap) => new(tileMap);
}
