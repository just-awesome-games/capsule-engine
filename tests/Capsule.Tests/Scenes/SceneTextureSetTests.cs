using System.Numerics;
using Capsule.Assets;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

// What a composed scene asks the host to keep resident: what its document draws, what its
// placements' entities reach, and what its own registration carries.
public sealed class SceneTextureSetTests
{
    private static readonly TextureHandle Chest = new("props/chest", ".png");

    private static readonly TextureHandle Banner = new("ui/banner", ".png");

    [Fact]
    public void ADocumentsGridsAndPlacements_MakeTheScenesSet()
    {
        Scene scene = new(SceneFixtures.Content(
            SceneFixtures.Room(new EntityPlacement(1, "chest", 0f, 0f)),
            Chests()));

        Assert.Equal([SceneFixtures.Atlas, Chest], scene.TextureSet);
    }

    // A placement whose type no entity claims is a spawn fault, raised where the entity is built;
    // asking for its groups first must not pre-empt that message.
    [Fact]
    public void APlacementNoEntityClaims_StillFailsAsASpawn()
    {
        Assert.Throws<SpawnException>(() => new Scene(SceneFixtures.Content(
            SceneFixtures.RoomWithoutTerrain(new EntityPlacement(1, "wyvern", 0f, 0f)),
            Chests())));
    }

    [Fact]
    public void TheClasssOwnGroups_JoinWhatItsDocumentComposed()
    {
        SceneRegistry scenes = new(
            Chests(),
            [SceneRegistration.FromDocument(typeof(Vault), "vault", static content => new Vault(content), Banners)]);

        Scene scene = scenes.CreateFromDocument("vault", SceneFixtures.Room(new EntityPlacement(1, "chest", 0f, 0f)));

        Assert.Equal([SceneFixtures.Atlas, Chest, Banner], scene.TextureSet);
    }

    [Fact]
    public void ADeclaredSet_ReplacesEverythingDerived()
    {
        SceneRegistry scenes = new(
            Chests(),
            [SceneRegistration.FromDocument(typeof(Sealed), "sealed", static content => new Sealed(content), Banners)]);

        Scene scene = scenes.CreateFromDocument("sealed", SceneFixtures.Room(new EntityPlacement(1, "chest", 0f, 0f)));

        Assert.Equal([Banner], scene.TextureSet);
    }

    private static void Banners(List<TextureHandle> set) => set.Add(Banner);

    private static EntityRegistry Chests() =>
        new([new EntityRegistration("chest", static spawn => new SceneFixtures.Placed(spawn), static set => set.Add(Chest))]);

    private sealed class Vault(SceneContent content) : Scene(content);

    private sealed class Sealed(SceneContent content) : Scene(content)
    {
        protected internal override IReadOnlyList<TextureHandle>? ResidentTextures => [Banner];
    }
}
