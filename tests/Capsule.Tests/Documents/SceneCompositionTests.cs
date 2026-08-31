using System.Numerics;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Documents;

public sealed class SceneCompositionTests
{
    [Fact]
    public void EachPlacementBecomesOneEntity_InTheDocumentsOwnOrder_CarryingItsPlacementData()
    {
        SceneDocument room = SceneFixtures.Room(
            new EntityPlacement(2, "chest", 48f, 16f),
            new EntityPlacement(1, "player-spawn", 32f, 24f));

        Scene scene = SceneFixtures.RoomScene(
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
    public void ASubclassPassesItsContentThrough_AndReachesTheTerrainItComposed()
    {
        SceneDocument room = SceneFixtures.Room(new EntityPlacement(1, "chest", 48f, 16f));

        SceneFixtures.Room01 scene = new(SceneFixtures.Content(
            room,
            SceneFixtures.Registry(("chest", static spawn => new SceneFixtures.Placed(spawn)))));

        Assert.Same(scene.Terrain, scene.Entities[0]);
        Assert.Equal(new Vector2(3 * SceneFixtures.TileSize, 2 * SceneFixtures.TileSize), scene.Size);
        Assert.IsType<SceneFixtures.Placed>(scene.Entities[1]);
    }

    // A document with no tile-map entry is a scene of entities alone: nothing draws terrain and
    // the scene spans nothing until it sets its own size.
    [Fact]
    public void ADocumentWithNoTerrain_ComposesWithNoTileMapAndNoSize()
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.RoomWithoutTerrain(new EntityPlacement(1, "chest", 48f, 16f)),
            SceneFixtures.Registry(("chest", static spawn => new SceneFixtures.Placed(spawn))));

        Assert.Null(scene.FindFirst<TileMap>());
        Assert.Equal(Vector2.Zero, scene.Size);
        Assert.IsType<SceneFixtures.Placed>(Assert.Single(scene.Entities.ToArray()));
    }

    [Fact]
    public void ContentWithNoDocument_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Scene(default));
    }
}
