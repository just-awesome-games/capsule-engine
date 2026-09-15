using System.Numerics;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

public sealed class SpawnTests
{
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
        List<EntityRegistration> entries =
        [
            new("chest", static spawn => new SceneFixtures.Placed(spawn)),
            new("chest", static spawn => new SceneFixtures.Placed(spawn)),
        ];

        Assert.Throws<ArgumentException>(() => new EntityRegistry(entries));
    }

    // The engine composes a scene document's terrain entry itself, so no game class may claim
    // the type it is written under.
    [Fact]
    public void ARegistryClaimingTheReservedTerrainType_IsRejectedWhereItIsBuilt()
    {
        List<EntityRegistration> entries =
        [
            new(SceneDocument.TileMapType, static spawn => new SceneFixtures.Placed(spawn)),
        ];

        ArgumentException failure = Assert.Throws<ArgumentException>(() => new EntityRegistry(entries));

        Assert.Contains("reserved", failure.Message, StringComparison.Ordinal);
    }
}
