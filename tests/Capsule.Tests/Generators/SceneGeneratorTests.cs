using System.Collections.Immutable;
using System.Reflection;
using Capsule.Scenes;
using Capsule.Tiles;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

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
        Assert.NotNull(compiled.GetTypeByMetadataName("Capsule.Generated.CapsuleScenes"));

        // The document names are the registry's contract with scene sources.
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleScenesFile);
        GeneratorHarness.AssertPairs(generated, "room-01", "Game.Room01");
        GeneratorHarness.AssertPairs(generated, "boss-arena", "Game.BossArena");
    }

    [Fact]
    public void ATypeKey_FixesTheClaimedDocumentAcrossAClassRename()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [TypeKey("room-01")]
            public sealed class OpeningRoom(SceneContent content) : Scene(content);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.NotNull(compiled.GetTypeByMetadataName("Game.OpeningRoom"));
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleScenesFile);
        GeneratorHarness.AssertPairs(generated, "room-01", "Game.OpeningRoom");
        Assert.DoesNotContain("opening-room", generated, StringComparison.Ordinal);
    }

    // AssetKeyTests holds every spelling the key rule refuses. This holds which refusal names which defect.
    [Theory]
    [InlineData("01-intro/opening", "CAP021", "'01-intro'")]
    [InlineData("rooms/nul", "CAP006", "'rooms/nul'")]
    public void AnUnsafeExplicitDocumentName_FailsTheBuildNamingTheTypeAndTheKey(string documentName, string id, string named)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [TypeKey("{{documentName}}")]
            public sealed class OpeningRoom(SceneContent content) : Scene(content);
            """).Diagnostics;

        Diagnostic rejected = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal(id, rejected.Id);
        Assert.Contains("Game.OpeningRoom", rejected.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(named, rejected.GetMessage(), StringComparison.Ordinal);
    }

    // A claim is authored prose. It is keyed like the document's own path: whatever spelling the
    // attribute carries, the class meets its document at the key the build ships it under.
    [Fact]
    public void AnExplicitDocumentName_IsNormalizedLikeTheDocumentsOwnPath()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [TypeKey("Stage1/Room01")]
            public sealed class OpeningRoom(SceneContent content) : Scene(content);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleScenesFile);
        GeneratorHarness.AssertPairs(generated, "stage-1/room-01", "Game.OpeningRoom");
        Assert.DoesNotContain("Stage1/Room01", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void AParameterlessConstructor_RegistersASceneNoDocumentBacks()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class MainMenu : Scene;
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleScenesFile);
        Assert.Contains("Game.MainMenu", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"main-menu\"", generated, StringComparison.Ordinal);
    }

    // Declaration order among two classes sharing a key does not decide which a baseScene resolves to.
    // A class that could serve as a base wins the key over one that never could. The ineligible
    // "Game.PlayableRoom" declares ahead of the eligible "Game.Z.PlayableRoom".
    [Fact]
    public void AnEligibleBaseScene_ResolvesOverASameKeyedIneligibleClassDeclaredFirst()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            """
            using Capsule.Scenes;

            namespace Game
            {
                public sealed class PlayableRoom(SceneContent content) : Scene(content);
            }

            namespace Game.Z
            {
                public abstract class PlayableRoom(SceneContent content) : Scene(content);
            }
            """,
            logic: true,
            ("scenes/halls/hall.scene.json", """{"baseScene": "playable-room", "entities": []}"""));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Assembly assembly = GeneratorHarness.Loaded(compiled);
        Type registryHolder = assembly.GetType("Capsule.Generated.CapsuleScenes")!;
        SceneRegistry registry = (SceneRegistry)registryHolder.GetProperty("Registry")!.GetValue(null)!;

        SceneDocument document = SceneDocument.Parse(
            """{"entities": [{"type": "tile-map", "tileSize": 16, "width": 1, "height": 1, "tileTypes": [{"name": "empty"}], "tiles": [0]}]}""");
        Scene composed = registry.Create(new SceneKey("scenes/halls/hall"), document);

        Assert.True(assembly.GetType("Game.Z.PlayableRoom")!.IsInstanceOfType(composed));
    }

    // The build hands every shipped document to the generator the way it hands textures, audio and
    // fonts. One a class claims emits FromDocument. One no class claims emits DocumentOnly.
    [Fact]
    public void AShippedSceneDocumentNoClassClaims_EmitsADocumentOnlyRegistration()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            $$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Room01(SceneContent content) : Scene(content);
            """,
            logic: true,
            ("room-01.scene.json", null),
            ("scenes/halls/hall.scene.json", null));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleScenesFile);
        Assert.Contains("SceneRegistration.FromDocument(typeof(global::Game.Room01), \"room-01\"", generated, StringComparison.Ordinal);
        Assert.Contains("SceneRegistration.DocumentOnly(\"scenes/halls/hall\"", generated, StringComparison.Ordinal);
    }

    // A document naming a base its own class also claims is a contradiction, not a precedence
    // question between the two. It refuses rather than picking one.
    [Fact]
    public void ADocumentNamingABaseSceneAClassAlsoClaims_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileAgainstSources(
            $$"""
            {{GeneratorHarness.Preamble}}

            public abstract class PlayableRoom(SceneContent content) : Scene(content);

            public sealed class Room01(SceneContent content) : Scene(content);
            """,
            logic: true,
            ("room-01.scene.json", """{"baseScene": "playable-room", "entities": []}""")).Diagnostics;

        Diagnostic error = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal("CAP027", error.Id);
        Assert.Contains("Game.Room01", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // A concrete class cannot take the sealed subclass the generator emits. A key no class claims is another defect.
    [Theory]
    [InlineData("playable-room", "CAP028", "Game.PlayableRoom")]
    [InlineData("missing-room", "CAP030", "missing-room")]
    public void ABaseSceneNamingNoAbstractScene_FailsTheBuild(string baseScene, string id, string named)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileAgainstSources(
            $$"""
            {{GeneratorHarness.Preamble}}

            public sealed class PlayableRoom(SceneContent content) : Scene(content);
            """,
            logic: true,
            ("scenes/halls/hall.scene.json", $$"""{"baseScene": "{{baseScene}}", "entities": []}""")).Diagnostics;

        Diagnostic error = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal(id, error.Id);
        Assert.Contains(named, error.GetMessage(), StringComparison.Ordinal);
    }

    // An abstract base with no document of its own needs no template subclass. The document that
    // names it composes into an instance carrying the document's entities.
    [Theory]
    [InlineData("", "playable-room")]
    [InlineData("[TypeKey(\"rooms/playable\")]", "rooms/playable")]
    // A [TypeKey] is keyed by one rule wherever it is read. Its document claim and its baseScene meet
    // at the same key.
    [InlineData("[TypeKey(\"Rooms/Playable\")]", "rooms/playable")]
    public void ADocumentsBaseScene_ComposesAnInstanceOfItCarryingTheDocumentsEntities(string attribute, string baseScene)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            $$"""
            {{GeneratorHarness.Preamble}}

            {{attribute}}
            public abstract class PlayableRoom(SceneContent content) : Scene(content);
            """,
            logic: true,
            ("scenes/halls/hall.scene.json", $$"""{"baseScene": "{{baseScene}}", "entities": []}"""));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Assembly assembly = GeneratorHarness.Loaded(compiled);
        Type registryHolder = assembly.GetType("Capsule.Generated.CapsuleScenes")!;
        SceneRegistry registry = (SceneRegistry)registryHolder.GetProperty("Registry")!.GetValue(null)!;

        SceneDocument document = SceneDocument.Parse(
            """{"entities": [{"type": "tile-map", "tileSize": 16, "width": 1, "height": 1, "tileTypes": [{"name": "empty"}], "tiles": [0]}]}""");
        Scene composed = registry.Create(new SceneKey("scenes/halls/hall"), document);

        Assert.True(assembly.GetType("Game.PlayableRoom")!.IsInstanceOfType(composed));
        Assert.IsType<TileMap>(composed.Entities[0]);
    }

    [Fact]
    public void BothRegistriesAreEmitted_WhenTheAssemblyDeclaresNothingToRegister()
    {
        Compilation compiled = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Bookkeeping;
            """).Updated;

        Assert.NotNull(compiled.GetTypeByMetadataName("Capsule.Generated.CapsuleEntities"));
        Assert.NotNull(compiled.GetTypeByMetadataName("Capsule.Generated.CapsuleScenes"));
        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));
    }
}
