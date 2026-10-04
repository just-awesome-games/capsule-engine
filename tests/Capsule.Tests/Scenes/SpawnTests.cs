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
            new SceneDocumentEntry("wyvern", 0f, 0f)));

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
}
