using System.Numerics;
using Capsule.Tiles;

namespace Capsule.Scenes.Documents;

// Which of the two shapes a SceneDocumentEntry holds.
internal enum SceneEntryKind
{
    Entity,
    TileMap,
}

/// <summary>One entry in a scene document's ordered list, holding either shape without boxing.</summary>
public readonly record struct SceneDocumentEntry
{
    private readonly float _scaleX;
    private readonly float _scaleY;
    private readonly string? _type;
    private readonly TileGrid? _grid;

    private SceneDocumentEntry(EntityPlacement entity)
    {
        Kind = SceneEntryKind.Entity;
        Id = entity.Id;
        X = entity.X;
        Y = entity.Y;
        _scaleX = entity.ScaleX;
        _scaleY = entity.ScaleY;
        ZIndex = entity.ZIndex;
        ScrollFactor = entity.ScrollFactor;
        _type = entity.Type;
    }

    private SceneDocumentEntry(TileMapPlacement tileMap)
    {
        Kind = SceneEntryKind.TileMap;
        Id = tileMap.Id;

        // A tile map is anchored and unscaled, and an identity scale passes every scale check.
        _scaleX = 1f;
        _scaleY = 1f;
        ZIndex = tileMap.ZIndex;
        ScrollFactor = tileMap.ScrollFactor;
        _grid = tileMap.Grid;
    }

    // Which shape this entry holds.
    internal SceneEntryKind Kind { get; }

    /// <summary>The entry's id in the document's single id space.</summary>
    public int Id { get; }

    /// <summary>The entry's authored world-space X coordinate, zero for a tile map.</summary>
    public float X { get; }

    /// <summary>The entry's authored world-space Y coordinate, zero for a tile map.</summary>
    public float Y { get; }

    /// <summary>The entry's authored draw band, or null when it authors none.</summary>
    public int? ZIndex { get; }

    /// <summary>The entry's authored scroll factor, or null when it authors none.</summary>
    public Vector2? ScrollFactor { get; }

    /// <summary>The game-defined entity placement, or null when this is a tile map.</summary>
    public EntityPlacement? Entity =>
        Kind == SceneEntryKind.Entity ? new EntityPlacement(Id, _type!, X, Y, _scaleX, _scaleY, ZIndex, ScrollFactor) : null;

    /// <summary>The engine-native tile-map placement, or null when this is a game entity.</summary>
    public TileMapPlacement? TileMap =>
        Kind == SceneEntryKind.TileMap ? new TileMapPlacement(Id, _grid!, ZIndex, ScrollFactor) : null;

    /// <summary>Wraps a game-defined entity placement as an ordered document entry.</summary>
    public static implicit operator SceneDocumentEntry(EntityPlacement entity) => new(entity);

    /// <summary>Wraps an engine-native tile-map placement as an ordered document entry.</summary>
    public static implicit operator SceneDocumentEntry(TileMapPlacement tileMap) => new(tileMap);
}
