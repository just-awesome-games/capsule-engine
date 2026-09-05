using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

// What the build derives as a scene's or an entity's residency groups, from the code it reaches.
// A group is one generated directory class's set, so the assertions read the emitted references.
public sealed class TextureResidencyGeneratorTests
{
    private static readonly string[] Textures =
    [
        "textures/hud.png",
        "textures/actors/hero.png",
        "textures/enemies/bat.png",
        "textures/fx/shot.png",
    ];

    [Fact]
    public void AHandleNamedFromCode_PullsInTheDirectoryThatDeclaresIt()
    {
        string generated = Entities("""
            public sealed class Bat : Entity
            {
                public Bat(EntitySpawn spawn)
                    : base(spawn.Position) => Skin = GameAssets.Textures.Enemies.Bat;

                private Capsule.Assets.TextureHandle Skin { get; }
            }
            """);

        Assert.Contains("GameAssets.Textures.Enemies.All", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("GameAssets.Textures.Fx.All", generated, StringComparison.Ordinal);
    }

    // A handle filed loose under the domain root has the whole registry for its group.
    [Fact]
    public void AHandleAtTheDomainRoot_PullsInTheRootSet()
    {
        string generated = Entities("""
            public sealed class Marker(EntitySpawn spawn) : Entity(spawn.Position)
            {
                private static readonly Capsule.Assets.TextureHandle Face = GameAssets.Textures.Hud;
            }
            """);

        Assert.Contains("GameAssets.Textures.All", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectorysOwnSet_IsThatDirectorysGroup()
    {
        string generated = Entities("""
            public sealed class Swarm(EntitySpawn spawn) : Entity(spawn.Position)
            {
                private static System.ReadOnlySpan<Capsule.Assets.TextureHandle> Skins => GameAssets.Textures.Enemies.All;
            }
            """);

        Assert.Contains("GameAssets.Textures.Enemies.All", generated, StringComparison.Ordinal);
    }

    // The ruling's own example: a player that spawns a buster that draws an effect keeps the
    // effect's directory resident, though the player names nothing of it.
    [Fact]
    public void AReferenceThroughASpawnedType_IsClosedOverTransitively()
    {
        string generated = Entities("""
            public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position)
            {
                private static Buster Fire() => new Buster(new Vector2(0f, 0f));
            }

            public sealed class Buster(Vector2 at) : Entity(at)
            {
                private static readonly Capsule.Assets.TextureHandle Shot = GameAssets.Textures.Fx.Shot;
            }
            """);

        Assert.Contains("GameAssets.Textures.Fx.All", generated, StringComparison.Ordinal);
    }

    // Sprite sheets and other derived registries construct handles rather than naming the asset
    // registry, and a scene drawing one of their frames still needs the directory resident.
    [Fact]
    public void AHandleConstructedFromLiterals_IsGroupedByItsDirectory()
    {
        string generated = Entities("""
            public sealed class Hero(EntitySpawn spawn) : Entity(spawn.Position)
            {
                private static readonly Capsule.Assets.TextureHandle Frame =
                    new Capsule.Assets.TextureHandle("actors/hero", ".png");
            }
            """);

        Assert.Contains("GameAssets.Textures.Actors.All", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void AClassReachingNoTexture_CarriesNoSet()
    {
        string generated = Entities("public sealed class Trigger(EntitySpawn spawn) : Entity(spawn.Position);");

        Assert.DoesNotContain(".All", generated, StringComparison.Ordinal);
    }

    // Residency is per scene, so the boot scene does not become resident in every scene it can
    // reach — a transition is where the next scene's set is loaded.
    [Fact]
    public void ASceneNamingAnotherScene_DoesNotInheritItsGroups()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstAssets(
            $$"""
            {{Preamble}}

            public sealed class Menu : Scene
            {
                protected override void OnStep(in StepContext context) => RequestScene<Arena>();
            }

            public sealed class Arena : Scene
            {
                private static readonly Capsule.Assets.TextureHandle Wall = GameAssets.Textures.Enemies.Bat;
            }
            """,
            Textures);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.GameScenesFile);

        // One builder, and it belongs to the arena: the menu's registration passes none.
        Assert.Contains("new global::Game.Menu()),", generated, StringComparison.Ordinal);
        Assert.Contains("GameAssets.Textures.Enemies.All", generated, StringComparison.Ordinal);
    }

    private const string Preamble = """
        using System.Numerics;
        using Capsule;
        using Capsule.Assets.Generated;
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;
        """;

    private static string Entities(string declarations)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstAssets(
            $$"""
            {{Preamble}}

            {{declarations}}
            """,
            Textures);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        return GeneratorHarness.Emitted(compiled, GeneratorHarness.GameEntitiesFile);
    }
}
