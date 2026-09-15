using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class CapsuleBootGeneratorTests
{
    private const string LogicSource = """
        using Capsule.Scenes;

        namespace Game;

        public sealed class Room01(SceneContent content) : Scene(content);
        """;

    private const string ShellSource = """
        using Capsule.Runtime.Generated;

        namespace Shell;

        public static class Program
        {
            public static void Boot() => CapsuleBoot.Configure("Spec Game").WithWindow(320, 180);
        }
        """;

    [Fact]
    public void TheShell_BootsThroughTheRegistryOfTheGameAssemblyItReferences()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.CompileShell(ShellSource, LogicSource);

        // The shell's own source calls CapsuleBoot.Configure, so a clean compilation is the entry
        // point standing up over the referenced assembly's registry.
        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        Assert.NotNull(updated.GetTypeByMetadataName("Capsule.Runtime.Generated.CapsuleBoot"));
    }

    [Fact]
    public void TheShell_GetsNoRegistryOfItsOwn_ThoughItSeesCapsuleScenesThroughTheGame()
    {
        Compilation updated = GeneratorHarness.CompileShell(ShellSource, LogicSource).Updated;

        Assert.NotNull(updated.GetTypeByMetadataName("Capsule.Scenes.Scene"));
        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.CapsuleScenesFile));
        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.CapsuleEntitiesFile));
    }

    [Fact]
    public void TheGameAssembly_GetsNoEntryPoint_ThoughItSeesTheEngineHost()
    {
        Compilation updated = GeneratorHarness.Compile(LogicSource).Updated;

        Assert.NotNull(updated.GetTypeByMetadataName("Capsule.Runtime.CapsuleEngine"));
        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.CapsuleBootFile));
    }

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

            internal sealed class Opening(SceneContent content) : Scene(content);
            """;

        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) =
            GeneratorHarness.CompileShellWithLogicAssemblies(
                ShellSource,
                ("Game.Actors", actors),
                ("Game.Rooms", rooms));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));

        // Each referenced logic assembly hands its registry over through a provider of its own.
        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleBootFile);
        Assert.Contains("CapsuleRegistryProvider_Game_Actors_", generated, StringComparison.Ordinal);
        Assert.Contains("CapsuleRegistryProvider_Game_Rooms_", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShell_TakesDriversFromItsLogicAssembliesAndFromItsOwnCode()
    {
        const string logic = """
            using Capsule.Input;
            using Capsule.Scenes;

            namespace Game;

            public sealed class Room01(SceneContent content) : Scene(content);

            public sealed class Walkthrough : IInputDriver
            {
                public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
                {
                    snapshot = DeviceSnapshot.Empty;
                    return tick < 60;
                }
            }
            """;
        const string shell = """
            using Capsule.Input;
            using Capsule.Runtime.Generated;
            using Capsule.Scenes;

            namespace Shell;

            public sealed class Idler : IInputDriver
            {
                public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
                {
                    snapshot = DeviceSnapshot.Empty;
                    return tick < 1;
                }
            }

            public static class Program
            {
                public static void Boot() => CapsuleBoot.Configure("Spec Game").WithWindow(320, 180);
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.CompileShell(shell, logic);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));

        // The logic assembly's drivers arrive through its provider; the shell's own is registered
        // in the entry point itself, under its class name.
        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleBootFile);
        Assert.Contains("CapsuleRegistryProvider_GameSpecs_", generated, StringComparison.Ordinal);
        GeneratorHarness.AssertPairs(generated, "Idler", "Shell.Idler");
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
    public void DuplicateDocumentClaimsAcrossLogicAssemblies_FailTheShellBuild()
    {
        const string first = """
            using Capsule.Scenes;
            namespace First;
            [SceneDocument("opening")]
            public sealed class FirstOpening(SceneContent content) : Scene(content);
            """;
        const string second = """
            using Capsule.Scenes;
            namespace Second;
            [SceneDocument("opening")]
            public sealed class SecondOpening(SceneContent content) : Scene(content);
            """;

        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileShellWithLogicAssemblies(
            ShellSource,
            ("Game.First", first),
            ("Game.Second", second)).Diagnostics;

        Assert.Equal("CAP005", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }
}
