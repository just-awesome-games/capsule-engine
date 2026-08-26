using System.Numerics;
using Capsule.Maps;
using Capsule.Scenes;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The scene a map composes into: the terrain first — the contract <see cref="TileMapTests"/>
/// asserts over — then the map's own objects, turned into spawn data here and nowhere else. A
/// subclass gets all of it by passing its context on, and reaches the map and the terrain it was
/// built from.
/// </summary>
public sealed class MapSceneTests
{
    [Fact]
    public void EachMapObjectBecomesOneEntity_InTheMapsOwnOrder_CarryingItsMapData()
    {
        Map room = SceneFixtures.Room(
            new MapObject(2, "chest", 48f, 16f),
            new MapObject(1, "player-spawn", 32f, 24f));

        MapScene scene = SceneFixtures.RoomScene(
            room,
            SceneFixtures.Registry(
                ("chest", static spawn => new SceneFixtures.Placed(spawn)),
                ("player-spawn", static spawn => new SceneFixtures.Placed(spawn))));

        Entity[] entities = scene.Entities.ToArray();

        // The tilemap is the scene's first entity; the map's own objects follow it in file order.
        Assert.Equal(3, entities.Length);
        Assert.IsType<TileMap>(entities[0]);
        Assert.Equal(
            new EntitySpawn(2, "chest", new Vector2(48f, 16f)),
            Assert.IsType<SceneFixtures.Placed>(entities[1]).Spawn);
        Assert.Equal(
            new EntitySpawn(1, "player-spawn", new Vector2(32f, 24f)),
            Assert.IsType<SceneFixtures.Placed>(entities[2]).Spawn);
    }

    [Fact]
    public void ASubclassPassesItsContextThrough_AndReachesTheMapAndItsTerrain()
    {
        Map room = SceneFixtures.Room(new MapObject(1, "chest", 48f, 16f));

        SceneFixtures.Room01 scene = new(SceneFixtures.Context(
            room,
            SceneFixtures.Registry(("chest", static spawn => new SceneFixtures.Placed(spawn)))));

        Assert.Same(room, scene.Composed);
        Assert.Same(scene.Terrain, scene.Entities[0]);
        Assert.Equal(new Vector2(3 * SceneFixtures.TileSize, 2 * SceneFixtures.TileSize), scene.Size);
        Assert.IsType<SceneFixtures.Placed>(scene.Entities[1]);
    }

    // The context is a struct, so a caller can hand over one that names no map at all.
    [Fact]
    public void AContextWithNoMap_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new MapScene(default));
    }
}
