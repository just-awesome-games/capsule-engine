using System.Numerics;
using Capsule.Maps;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>A map's tile grid followed by its spawned entities, preserving authored order.</summary>
public class MapScene : Scene
{
    /// <exception cref="SpawnException">A map object's spawn type is claimed by no entity.</exception>
    public MapScene(MapSceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context.Map);

        Map = context.Map;
        Tiles = new TileMap(context.Map.Grid);
        Add(Tiles);
        Size = Tiles.Size;
        Spawn(SpawnsOf(context.Map), context.Entities);
    }

    /// <summary>The map this scene was composed from.</summary>
    protected Map Map { get; }

    /// <summary>The map's terrain: this scene's first entity, and the grid to query.</summary>
    protected TileMap Tiles { get; }

    private static EntitySpawn[] SpawnsOf(Map map)
    {
        ReadOnlySpan<MapObject> objects = map.Objects;
        EntitySpawn[] spawns = new EntitySpawn[objects.Length];

        for (int index = 0; index < objects.Length; index++)
        {
            MapObject placed = objects[index];
            spawns[index] = new EntitySpawn(placed.Id, placed.Type, new Vector2(placed.X, placed.Y));
        }

        return spawns;
    }
}
