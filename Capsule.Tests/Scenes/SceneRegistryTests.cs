using Capsule.Scenes;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The table the engine boots through, indexed both ways from one set of registrations: by class,
/// for a game that names the scene it starts on, and by map name, for one that names the map. A
/// map no class claims is not a failure — it is the case that makes a per-map class optional.
/// </summary>
public sealed class SceneRegistryTests
{
    private static readonly EntityRegistry NoEntities = SceneFixtures.Registry();

    [Fact]
    public void ASceneIsFoundByItsClass_AndAMapBackedOneNamesItsMap()
    {
        SceneRegistry scenes = Registry(Room01, Menu);

        Assert.Equal("room-01", scenes.MapNameOf(typeof(SceneFixtures.Room01)));
        Assert.Null(scenes.MapNameOf(typeof(SceneFixtures.HookScene)));
        Assert.IsType<SceneFixtures.HookScene>(scenes.Create(typeof(SceneFixtures.HookScene)));
    }

    [Fact]
    public void AMapIsComposedIntoTheClassClaimingIt()
    {
        SceneRegistry scenes = Registry(Room01, Menu);

        Scene composed = scenes.CreateForMap("room-01", SceneFixtures.Room());

        Assert.IsType<SceneFixtures.Room01>(composed);
    }

    // A per-map class is optional, and this is what that cashes out as.
    [Fact]
    public void AMapNoClassClaims_ComposesIntoAPlainMapScene()
    {
        SceneRegistry scenes = Registry(Room01, Menu);

        Scene composed = scenes.CreateForMap("attic", SceneFixtures.Room());

        Assert.Equal(typeof(MapScene), composed.GetType());
        Assert.IsType<TileMap>(composed.Entities[0]);
    }

    [Fact]
    public void AnUnregisteredClass_NamesItselfAndWhatIsRegistered()
    {
        SceneRegistry scenes = Registry(Room01);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => scenes.MapNameOf(typeof(SceneFixtures.HookScene)));

        Assert.Contains("HookScene", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Room01", failure.Message, StringComparison.Ordinal);
    }

    // Constructing one without its map would produce a scene with no terrain and no objects,
    // which is not what the class says it is.
    [Fact]
    public void AMapBackedClassCannotBeBuiltWithoutItsMap()
    {
        SceneRegistry scenes = Registry(Room01);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => scenes.Create(typeof(SceneFixtures.Room01)));

        Assert.Contains("room-01", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneClassRegisteredTwice_IsRejectedWhereTheRegistryIsBuilt()
    {
        Assert.Throws<ArgumentException>(() => Registry(Room01, Room01));
    }

    [Fact]
    public void TwoClassesClaimingOneMap_AreRejectedWhereTheRegistryIsBuilt()
    {
        SceneRegistration menuOnRoom01 = SceneRegistration.MapBacked(
            typeof(SceneFixtures.HookScene),
            "room-01",
            static context => new SceneFixtures.Room01(context));

        Assert.Throws<ArgumentException>(() => Registry(Room01, menuOnRoom01));
    }

    [Fact]
    public void ARegistrationNamingNoClass_IsRejectedWhereTheRegistryIsBuilt()
    {
        Assert.Throws<ArgumentException>(() => Registry(default(SceneRegistration)));
    }

    private static SceneRegistration Room01 => SceneRegistration.MapBacked(
        typeof(SceneFixtures.Room01),
        "room-01",
        static context => new SceneFixtures.Room01(context));

    private static SceneRegistration Menu => SceneRegistration.Plain(
        typeof(SceneFixtures.HookScene),
        static () => new SceneFixtures.HookScene());

    private static SceneRegistry Registry(params SceneRegistration[] scenes) => new(NoEntities, scenes);
}
