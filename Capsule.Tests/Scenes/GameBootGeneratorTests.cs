using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The shell half of the generator, over the two compilations a game really has. What is asserted
/// is that the entry point reaches the registry across the project reference, that the shell gets
/// no registry of its own though it sees Capsule.Scenes through the game assembly, that the game
/// assembly gets no entry point though it sees the engine host, and that a shell finding no
/// registry still hands its <c>Program</c> a builder that compiles.
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
            public static void Boot() => GameBoot.Configure().WithWindow("Spec", 320, 180);
        }
        """;

    [Fact]
    public void TheShell_BootsThroughTheRegistryOfTheGameAssemblyItReferences()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.CompileShell(ShellSource, LogicSource);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        Assert.Contains(
            ".WithScenes(global::Capsule.Scenes.Generated.GameScenes.Registry)",
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

    // A shell running a hand-written simulation references no game assembly and needs no registry,
    // so the entry point is emitted either way and the shell's Program compiles the same.
    [Fact]
    public void AShellReferencingNoGeneratedRegistry_BootsUnwired()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.CompileShell(ShellSource);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        Assert.DoesNotContain(
            "WithScenes",
            GeneratorHarness.Emitted(updated, GeneratorHarness.GameBootFile),
            StringComparison.Ordinal);
    }
}
