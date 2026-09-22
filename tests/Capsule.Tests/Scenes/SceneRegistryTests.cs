using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using Capsule.Tiles;

namespace Capsule.Tests.Scenes;

public sealed class SceneRegistryTests
{
    private static readonly EntityRegistry NoEntities = SceneFixtures.Registry();

    [Fact]
    public void APlainSceneIsFoundByItsClass()
    {
        SceneRegistry scenes = Registry(Menu);

        Assert.Null(scenes.DocumentNameOf(typeof(SceneFixtures.HookScene)));
        Assert.IsType<SceneFixtures.HookScene>(scenes.Create(typeof(SceneFixtures.HookScene)));
    }

    [Fact]
    public void ADocumentBackedSceneNamesItsDocument_AndIsNotBuiltByClassAlone()
    {
        SceneRegistry scenes = Registry(Menu, Room01);

        Assert.Equal("room-01", scenes.DocumentNameOf(typeof(SceneFixtures.Room01)));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => scenes.Create(typeof(SceneFixtures.Room01)));

        Assert.Contains("room-01", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentIsComposedIntoTheClassClaimingIt()
    {
        SceneRegistry scenes = Registry(Room01);

        Assert.IsType<SceneFixtures.Room01>(scenes.CreateFromDocument("room-01", SceneFixtures.Room()));
    }

    [Fact]
    public void ADocumentNoClassClaims_ComposesIntoAPlainScene()
    {
        SceneRegistry scenes = Registry(Room01);

        Scene composed = scenes.CreateFromDocument("attic", SceneFixtures.Room());

        Assert.Equal(typeof(Scene), composed.GetType());
        Assert.IsType<TileMap>(composed.Entities[0]);
    }

    [Fact]
    public void AnUnregisteredClass_NamesItselfAndWhatIsRegistered()
    {
        SceneRegistry scenes = Registry(Menu);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => scenes.Create(typeof(SceneFixtures.SpawnScene)));

        Assert.Contains("SpawnScene", failure.Message, StringComparison.Ordinal);
        Assert.Contains("HookScene", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneClassRegisteredTwice_IsRejectedWhereTheRegistryIsBuilt()
    {
        Assert.Throws<ArgumentException>(() => Registry(Menu, Menu));
    }

    [Fact]
    public void TwoClassesClaimingOneDocument_AreRejectedWhereTheRegistryIsBuilt()
    {
        SceneRegistration hookSceneOnRoom01 = SceneRegistration.FromDocument(
            typeof(SceneFixtures.HookScene),
            "room-01",
            static content => new SceneFixtures.Room01(content!.Value));

        Assert.Throws<ArgumentException>(() => Registry(Room01, hookSceneOnRoom01));
    }

    [Fact]
    public void ARegistrationNamingNeitherAClassNorADocument_IsRejectedWhereTheRegistryIsBuilt()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => Registry(default(SceneRegistration)));

        Assert.Contains("names neither a class nor a document", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentOnlyRegistration_IsRegisteredAndComposesAPlainScene()
    {
        SceneRegistration attic = SceneRegistration.DocumentOnly(
            "attic", static content => new Scene(content!.Value));
        SceneRegistry scenes = Registry(attic);

        Assert.Equal(attic, Assert.Single(scenes.Registrations));
        Assert.IsType<TileMap>(scenes.CreateFromDocument("attic", SceneFixtures.Room()).Entities[0]);
    }

    [Fact]
    public void AnUnregisteredClass_NamesWhatIsRegisteredByClassAlone_NotADocumentOnlyRegistration()
    {
        SceneRegistration attic = SceneRegistration.DocumentOnly(
            "attic", static content => new Scene(content!.Value));
        SceneRegistry scenes = Registry(Menu, attic);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => scenes.Create(typeof(SceneFixtures.SpawnScene)));

        Assert.Contains("HookScene", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("attic", failure.Message, StringComparison.Ordinal);
    }

    private static SceneRegistration Menu => SceneRegistration.Plain(
        typeof(SceneFixtures.HookScene),
        static _ => new SceneFixtures.HookScene());

    private static SceneRegistration Room01 => SceneRegistration.FromDocument(
        typeof(SceneFixtures.Room01),
        "room-01",
        static content => new SceneFixtures.Room01(content!.Value));

    private static SceneRegistry Registry(params SceneRegistration[] scenes) => new(NoEntities, scenes);
}
