using System.Numerics;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

/// <summary>
/// Turning spawn data into a scene's entities, with no authoring format in sight: in the order
/// given, carrying the data as handed over, and with a failure that names what went wrong rather
/// than drawing nothing.
/// </summary>
public sealed class SpawnTests
{
    [Fact]
    public void SpawnsBecomeEntities_InTheOrderGiven_CarryingTheirData()
    {
        EntitySpawn chestSpawn = new(2, "chest", new Vector2(48f, 16f));
        EntitySpawn playerSpawn = new(1, "player-spawn", new Vector2(32f, 24f));

        SceneFixtures.SpawnScene scene = new(
            SceneFixtures.Registry(
                ("chest", static spawn => new SceneFixtures.Placed(spawn)),
                ("player-spawn", static spawn => new SceneFixtures.Placed(spawn))),
            chestSpawn,
            playerSpawn);

        Entity[] entities = scene.Entities.ToArray();

        Assert.Equal(2, entities.Length);
        SceneFixtures.Placed chest = Assert.IsType<SceneFixtures.Placed>(entities[0]);
        SceneFixtures.Placed player = Assert.IsType<SceneFixtures.Placed>(entities[1]);
        Assert.Equal(chestSpawn, chest.Spawn);
        Assert.Equal(playerSpawn, player.Spawn);

        // The authored coordinate is the entity's to interpret, and it starts there untouched.
        Assert.Equal(new Vector2(48f, 16f), chest.Position);
        Assert.Equal(chest.Position, chest.PreviousPosition);
    }

    [Fact]
    public void AnUnregisteredSpawnType_NamesItselfAndWhatIsRegistered()
    {
        SpawnException failure = Assert.Throws<SpawnException>(() => new SceneFixtures.SpawnScene(
            SceneFixtures.Registry(
                ("chest", static spawn => new SceneFixtures.Placed(spawn)),
                ("player", static spawn => new SceneFixtures.Placed(spawn))),
            new EntitySpawn(1, "wyvern", Vector2.Zero)));

        Assert.Contains("wyvern", failure.Message, StringComparison.Ordinal);
        Assert.Contains("chest, player", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegistryWithARepeatedType_IsRejectedWhereItIsBuilt()
    {
        List<KeyValuePair<string, EntitySpawner>> entries =
        [
            new("chest", static spawn => new SceneFixtures.Placed(spawn)),
            new("chest", static spawn => new SceneFixtures.Placed(spawn)),
        ];

        Assert.Throws<ArgumentException>(() => new EntityRegistry(entries));
    }
}
