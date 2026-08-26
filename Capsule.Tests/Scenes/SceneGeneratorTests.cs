using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The scene half of the generated registries. A class's constructor shape is the whole opt-in and
/// says which of the two kinds it is; the map it is composed from is its kebab-cased name, so what
/// is asserted is that both shapes reach the registry under the right kind, that anything else is
/// passed over without a word, and that two classes cannot silently claim one map.
/// </summary>
public sealed class SceneGeneratorTests
{
    [Fact]
    public void AMapSceneContextConstructor_ComposesTheSceneFromItsKebabCasedName()
    {
        string generated = Generated($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Room01(MapSceneContext context) : MapScene(context);

            public sealed class BossArena(MapSceneContext context) : MapScene(context);
            """);

        // The digit is a word of its own, so Room01 boots room-01.map.json.
        Assert.Contains("MapBacked(typeof(global::Game.Room01), \"room-01\"", generated, StringComparison.Ordinal);
        Assert.Contains("MapBacked(typeof(global::Game.BossArena), \"boss-arena\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void AParameterlessConstructor_RegistersASceneNoMapBacks()
    {
        string generated = Generated($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class MainMenu : Scene;
            """);

        Assert.Contains("Plain(typeof(global::Game.MainMenu)", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed class Overlay : Scene { public Overlay(int depth) { } }")]
    [InlineData("public sealed class Overlay : Scene { private Overlay() { } }")]
    [InlineData("public abstract class Room : MapScene { protected Room(MapSceneContext context) : base(context) { } }")]
    [InlineData("public sealed class Overlay { public Overlay() { } }")]
    public void ASceneOfAnotherShape_IsPassedOverInSilence(string declaration)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            {{declaration}}
            """);

        Assert.Empty(diagnostics);

        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.GameScenesFile);
        Assert.DoesNotContain("Game.Overlay", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Game.Room", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoClassesDerivingOneMapName_FailTheBuildNamingBoth()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile("""
            using Capsule.Scenes;

            namespace Game
            {
                public sealed class Room01(MapSceneContext context) : MapScene(context);
            }

            namespace Game.Deep
            {
                public sealed class Room01(MapSceneContext context) : MapScene(context);
            }
            """).Diagnostics;

        Diagnostic collision = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal("CAP005", collision.Id);

        string message = collision.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Game.Room01", message, StringComparison.Ordinal);
        Assert.Contains("Game.Deep.Room01", message, StringComparison.Ordinal);
        Assert.Contains("'room-01'", message, StringComparison.Ordinal);
    }

    // The trap constructor discovery sets: an assembly with nothing to register still has call
    // sites naming both registries, and under an opt-in attribute their absence was self-evident.
    [Fact]
    public void BothRegistriesAreEmitted_WhenTheAssemblyDeclaresNothingToRegister()
    {
        Compilation compiled = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Bookkeeping;
            """).Updated;

        Assert.NotNull(compiled.GetTypeByMetadataName("Capsule.Scenes.Generated.GameEntities"));
        Assert.NotNull(compiled.GetTypeByMetadataName("Capsule.Scenes.Generated.GameScenes"));
        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));
    }

    [Fact]
    public void TheGeneratedRegistry_CompilesOverBothKindsOfScene()
    {
        Compilation compiled = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Room01(MapSceneContext context) : MapScene(context);

            public sealed class MainMenu : Scene;

            public sealed class Chest(EntitySpawn spawn) : Entity(spawn.Position);
            """).Updated;

        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));
    }

    private static string Generated(string source) => GeneratorHarness.Generated(source, GeneratorHarness.GameScenesFile);
}
