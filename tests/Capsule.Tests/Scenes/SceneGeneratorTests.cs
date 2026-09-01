using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

public sealed class SceneGeneratorTests
{
    [Fact]
    public void ASceneContentConstructor_ComposesTheSceneFromItsKebabCasedName()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Room01(SceneContent content) : Scene(content);

            public sealed class BossArena(SceneContent content) : Scene(content);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.NotNull(compiled.GetTypeByMetadataName("Capsule.Scenes.Generated.GameScenes"));

        // The document names are the registry's contract with scene sources; each class registers
        // under its own kebab-cased name.
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.GameScenesFile);
        AssertClaimedBy(generated, "room-01", "Game.Room01");
        AssertClaimedBy(generated, "boss-arena", "Game.BossArena");
    }

    // Every emitted line that claims the document names the type beside it, so the pairing is
    // asserted without pinning how a line is spelled.
    private static void AssertClaimedBy(string generated, string documentName, string type)
    {
        string[] claims = generated.Split((char)10)
            .Where(line => line.Contains($"\"{documentName}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(claims);
        Assert.All(claims, line => Assert.Contains(type, line, StringComparison.Ordinal));
    }

    [Fact]
    public void SceneDocument_FixesTheAuthoredIdentityAcrossAClassRename()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [SceneDocument("room-01")]
            public sealed class OpeningRoom(SceneContent content) : Scene(content);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.NotNull(compiled.GetTypeByMetadataName("Game.OpeningRoom"));
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.GameScenesFile);
        AssertClaimedBy(generated, "room-01", "Game.OpeningRoom");
        Assert.DoesNotContain("opening-room", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../room")]
    [InlineData("rooms/opening")]
    [InlineData("opening room")]
    public void AnUnsafeExplicitDocumentName_FailsTheBuild(string documentName)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [SceneDocument("{{documentName}}")]
            public sealed class OpeningRoom(SceneContent content) : Scene(content);
            """).Diagnostics;

        Assert.Equal("CAP006", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void SceneDocument_OnASceneWithoutAContentConstructor_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [SceneDocument("menu")]
            public sealed class MainMenu : Scene;
            """).Diagnostics;

        Assert.Equal("CAP007", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void ASceneWithBothRegistryConstructorShapes_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Room : Scene
            {
                public Room() { }
                public Room(SceneContent content) : base(content) { }
            }
            """).Diagnostics;

        Assert.Equal("CAP009", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void ARegisteredSceneNestedBehindPrivateAccess_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public static class Scenes
            {
                private sealed class Room(SceneContent content) : Scene(content);
            }
            """).Diagnostics;

        Assert.Equal("CAP008", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void AParameterlessConstructor_RegistersASceneNoDocumentBacks()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class MainMenu : Scene;
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.GameScenesFile);
        Assert.Contains("Game.MainMenu", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"main-menu\"", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed class Overlay : Scene { public Overlay(int depth) { } }")]
    [InlineData("public sealed class Overlay : Scene { private Overlay() { } }")]
    [InlineData("public abstract class Room : Scene { protected Room(SceneContent content) : base(content) { } }")]
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
    public void TwoClassesDerivingOneDocumentName_FailTheBuildNamingBoth()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile("""
            using Capsule.Scenes;

            namespace Game
            {
                public sealed class Room01(SceneContent content) : Scene(content);
            }

            namespace Game.Deep
            {
                public sealed class Room01(SceneContent content) : Scene(content);
            }
            """).Diagnostics;

        Diagnostic collision = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal("CAP005", collision.Id);

        string message = collision.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Game.Room01", message, StringComparison.Ordinal);
        Assert.Contains("Game.Deep.Room01", message, StringComparison.Ordinal);
        Assert.Contains("'room-01'", message, StringComparison.Ordinal);
    }

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

            public sealed class Room01(SceneContent content) : Scene(content);

            public sealed class MainMenu : Scene;

            public sealed class Chest(EntitySpawn spawn) : Entity(spawn.Position);
            """).Updated;

        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));
    }

    private static string Generated(string source) => GeneratorHarness.Generated(source, GeneratorHarness.GameScenesFile);
}
