using System.Numerics;
using Capsule.Levels;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

/// <summary>
/// Turning a level's entities into a scene's: in file order, with the level's own data, and
/// with a failure that names what went wrong rather than drawing nothing.
/// </summary>
public sealed class SpawnTests
{
    [Fact]
    public void LevelEntitiesSpawnInFileOrder_CarryingTheirLevelData()
    {
        Level room = SceneFixtures.Room(
            new LevelEntity(2, "chest", 48f, 16f),
            new LevelEntity(1, "player-spawn", 32f, 24f));

        SceneFixtures.LevelScene scene = new(
            room,
            SceneFixtures.Registry(
                ("chest", static spawn => new SceneFixtures.Placed(spawn)),
                ("player-spawn", static spawn => new SceneFixtures.Placed(spawn))));

        Entity[] entities = scene.Entities.ToArray();

        // The tilemap is the scene's first entity; the level's own follow it in file order.
        Assert.Equal(3, entities.Length);
        Assert.Same(scene.Tiles, entities[0]);
        SceneFixtures.Placed chest = Assert.IsType<SceneFixtures.Placed>(entities[1]);
        SceneFixtures.Placed player = Assert.IsType<SceneFixtures.Placed>(entities[2]);
        Assert.Equal(new EntitySpawn(2, "chest", new Vector2(48f, 16f)), chest.Spawn);
        Assert.Equal(new EntitySpawn(1, "player-spawn", new Vector2(32f, 24f)), player.Spawn);

        // The raw level coordinate is the entity's to interpret, and it starts there untouched.
        Assert.Equal(new Vector2(48f, 16f), chest.Position);
        Assert.Equal(chest.Position, chest.PreviousPosition);
    }

    [Fact]
    public void AnUnregisteredEntityType_NamesItselfAndWhatIsRegistered()
    {
        Level room = SceneFixtures.Room(new LevelEntity(1, "wyvern", 0f, 0f));

        SpawnException failure = Assert.Throws<SpawnException>(() => new SceneFixtures.LevelScene(
            room,
            SceneFixtures.Registry(
                ("chest", static spawn => new SceneFixtures.Placed(spawn)),
                ("player", static spawn => new SceneFixtures.Placed(spawn)))));

        Assert.Contains("wyvern", failure.Message, StringComparison.Ordinal);
        Assert.Contains("chest, player", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegistryWithARepeatedId_IsRejectedWhereItIsBuilt()
    {
        List<KeyValuePair<string, EntitySpawner>> entries =
        [
            new("chest", static spawn => new SceneFixtures.Placed(spawn)),
            new("chest", static spawn => new SceneFixtures.Placed(spawn)),
        ];

        Assert.Throws<ArgumentException>(() => new LevelTypeRegistry(entries));
    }
}
