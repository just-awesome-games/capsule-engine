using System.Numerics;
using Capsule.Maps;
using Capsule.Scenes;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

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

    [Fact]
    public void AContextWithNoMap_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new MapScene(default));
    }
}
