using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

public sealed class SceneGeneratorTests
{
    [Fact]
    public void ASceneContentConstructor_ComposesTheSceneFromItsKebabCasedName()
    {
        string generated = Generated($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Room01(SceneContent content) : Scene(content);

            public sealed class BossArena(SceneContent content) : Scene(content);
            """);

        Assert.Contains("FromDocument(typeof(global::Game.Room01), \"room-01\"", generated, StringComparison.Ordinal);
        Assert.Contains("FromDocument(typeof(global::Game.BossArena), \"boss-arena\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneDocument_FixesTheAuthoredIdentityAcrossAClassRename()
    {
        string generated = Generated($$"""
            {{GeneratorHarness.Preamble}}

            [SceneDocument("room-01")]
            public sealed class OpeningRoom(SceneContent content) : Scene(content);
            """);

        Assert.Contains("FromDocument(typeof(global::Game.OpeningRoom), \"room-01\"", generated, StringComparison.Ordinal);
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
        string generated = Generated($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class MainMenu : Scene;
            """);

        Assert.Contains("Plain(typeof(global::Game.MainMenu)", generated, StringComparison.Ordinal);
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
