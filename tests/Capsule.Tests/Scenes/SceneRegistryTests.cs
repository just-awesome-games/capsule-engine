using System.IO.Compression;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
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

        Assert.IsType<SceneFixtures.Room01>(scenes.Create(new SceneKey("room-01"), SceneFixtures.Room()));
    }

    [Fact]
    public void ADocumentNoClassClaims_ComposesIntoAPlainScene()
    {
        SceneRegistry scenes = Registry(Room01);

        Scene composed = scenes.Create(new SceneKey("attic"), SceneFixtures.Room());

        Assert.Equal(typeof(Scene), composed.GetType());
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
        Assert.Equal(typeof(Scene), scenes.Create(new SceneKey("attic"), SceneFixtures.Room()).GetType());
    }

    [Fact]
    public void AnUnregisteredClass_NamesItselfAndWhatIsRegisteredByClass()
    {
        SceneRegistration attic = SceneRegistration.DocumentOnly(
            "attic", static content => new Scene(content!.Value));
        SceneRegistry scenes = Registry(Menu, attic);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => scenes.Create(typeof(SceneFixtures.SpawnScene)));

        Assert.Contains("SpawnScene", failure.Message, StringComparison.Ordinal);
        Assert.Contains("HookScene", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("attic", failure.Message, StringComparison.Ordinal);
    }

    // Every shipped document composes as a run would load it, and one failure lists each document that does not.
    [Fact]
    public void ComposeAll_ListsEveryShippedDocumentThatDoesNotCompose()
    {
        string shipped = Path.Combine(AppContext.BaseDirectory, "assets", "compose-all");
        Directory.CreateDirectory(shipped);
        ShippedSceneDocument.Write(SceneFixtures.Room(), Path.Combine(shipped, "good.scene.json.gz"), CompressionLevel.Fastest);
        ShippedSceneDocument.Write(SceneFixtures.Room(new SceneDocumentEntry("wyvern", 0f, 0f)), Path.Combine(shipped, "bad.scene.json.gz"), CompressionLevel.Fastest);
        SceneRegistry scenes = Registry(
            SceneRegistration.DocumentOnly("compose-all/good", static content => new Scene(content!.Value)),
            SceneRegistration.DocumentOnly("compose-all/bad", static content => new Scene(content!.Value)));

        try
        {
            SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(scenes.ComposeAll);

            Assert.StartsWith("1 of 2 scene documents do not compose:\nscene document 'compose-all/bad': spawn type 'wyvern'", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("good", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(shipped, recursive: true);
        }
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
