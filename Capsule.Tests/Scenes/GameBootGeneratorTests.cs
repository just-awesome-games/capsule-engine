using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The shell half of the generator, over the two compilations a game really has. What is asserted
/// is that the entry point reaches the registry across the project reference, that the shell gets
/// no registry of its own though it sees Capsule.Scenes through the game assembly, that the game
/// assembly gets no entry point though it sees the engine host, and that a shell referencing no
/// game assembly at all fails the build rather than generating an entry point naming no scenes.
/// </summary>
public sealed class GameBootGeneratorTests
{
    private const string LogicSource = """
        using Capsule.Scenes;

        namespace Game;

        public sealed class Room01(MapSceneContext context) : MapScene(context);
        """;

    private const string ShellSource = """
        using Capsule.Runtime.Generated;

        namespace Shell;

        public static class Program
        {
            public static void Boot() => GameBoot.Configure("Spec Game").WithWindow(320, 180);
        }
        """;

    [Fact]
    public void TheShell_BootsThroughTheRegistryOfTheGameAssemblyItReferences()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.CompileShell(ShellSource, LogicSource);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        Assert.Contains(
            "CapsuleEngine.Configure(gameName, Scenes)",
            GeneratorHarness.Emitted(updated, GeneratorHarness.GameBootFile),
            StringComparison.Ordinal);
    }

    // The trap the roles exist for: a shell reaches Capsule.Scenes through the game assembly, so a
    // generator gated on what it can see would emit a second, empty pair of registries here — and
    // source shadows metadata, so the shell would boot through those instead.
    [Fact]
    public void TheShell_GetsNoRegistryOfItsOwn_ThoughItSeesCapsuleScenesThroughTheGame()
    {
        Compilation updated = GeneratorHarness.CompileShell(ShellSource, LogicSource).Updated;

        Assert.NotNull(updated.GetTypeByMetadataName("Capsule.Scenes.Scene"));
        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.GameScenesFile));
        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.GameEntitiesFile));
    }

    [Fact]
    public void TheGameAssembly_GetsNoEntryPoint_ThoughItSeesTheEngineHost()
    {
        Compilation updated = GeneratorHarness.Compile(LogicSource).Updated;

        Assert.NotNull(updated.GetTypeByMetadataName("Capsule.Runtime.CapsuleEngine"));
        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.GameBootFile));
    }

    // The shell role exists to hand a game's scenes to the engine, so a shell that references no
    // assembly declaring any is wired wrong. Diagnosed at compile time rather than left to fail on
    // the player's machine at the first RunScene.
    [Fact]
    public void AShellReferencingNoLogicAssembly_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileShell(ShellSource).Diagnostics;

        Assert.Equal("CAP015", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void TheShell_AggregatesEveryReferencedLogicAssembly()
    {
        const string actors = """
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace Actors;

            internal sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);
            """;
        const string rooms = """
            using Capsule.Scenes;

            namespace Rooms;

            internal sealed class Opening(MapSceneContext context) : MapScene(context);
            """;

        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) =
            GeneratorHarness.CompileShellWithLogicAssemblies(
                ShellSource,
                ("Game.Actors", actors),
                ("Game.Rooms", rooms));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));

        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.GameBootFile);
        Assert.Equal(2, generated.Split(".AddEntities(entities);", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, generated.Split(".AddScenes(scenes);", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void DuplicateSpawnClaimsAcrossLogicAssemblies_FailTheShellBuild()
    {
        const string first = """
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;
            namespace First;
            public sealed class Chest(EntitySpawn spawn) : Entity(spawn.Position);
            """;
        const string second = """
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;
            namespace Second;
            [SpawnType("chest")]
            public sealed class IronChest(EntitySpawn spawn) : Entity(spawn.Position);
            """;

        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileShellWithLogicAssemblies(
            ShellSource,
            ("Game.First", first),
            ("Game.Second", second)).Diagnostics;

        Assert.Equal("CAP003", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void DuplicateMapClaimsAcrossLogicAssemblies_FailTheShellBuild()
    {
        const string first = """
            using Capsule.Scenes;
            namespace First;
            [MapName("opening")]
            public sealed class FirstOpening(MapSceneContext context) : MapScene(context);
            """;
        const string second = """
            using Capsule.Scenes;
            namespace Second;
            [MapName("opening")]
            public sealed class SecondOpening(MapSceneContext context) : MapScene(context);
            """;

        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileShellWithLogicAssemblies(
            ShellSource,
            ("Game.First", first),
            ("Game.Second", second)).Diagnostics;

        Assert.Equal("CAP005", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }
}
