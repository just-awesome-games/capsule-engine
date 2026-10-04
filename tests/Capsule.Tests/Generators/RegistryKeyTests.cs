using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

/// <summary>
/// Where a type is declared is the key it claims: its namespace under the assembly's root, minus a
/// folder repeating the type's own name. An entity, camera, tile type or baseScene key drops a leading
/// <c>Entities</c>, <c>Cameras</c>, <c>Tiles</c> or <c>Scenes</c> segment, whichever kind the type is. A scene claims
/// the document at its namespace's path under <c>Assets/</c>, so <c>Game.Scenes.Room</c> composes
/// <c>Assets/Scenes/room.scene.json</c>.
/// </summary>
public sealed class RegistryKeyTests
{
    [Theory]
    [InlineData("Game.Entities", "Player", "player")]
    [InlineData("Game.Entities.Player", "Player", "player")]
    [InlineData("Game.Entities.Enemies", "Bat", "enemies/bat")]
    [InlineData("Game.Entities.Enemies.Cave", "Bat", "enemies/cave/bat")]
    [InlineData("Game", "Player", "player")]
    [InlineData("Game.Cameras", "CameraStart", "camera-start")]
    [InlineData("Game.Scenes.Doors", "Door", "doors/door")]
    [InlineData("Game.Tiles.Hazards", "Spikes", "hazards/spikes")]
    [InlineData("Vendor.Enemies", "Bat", "bat")]
    public void AnEntity_ClaimsTheKeyItsNamespaceNames(string space, string type, string key)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", $$"""
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace {{space}};

            public sealed class {{type}}(EntitySpawn spawn) : Entity(spawn);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains(
            $"\"{key}\"",
            GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleEntitiesFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TwoTypesOfOneNameInDifferentFolders_AreTwoKeys()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", """
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace Game.Entities.Enemies
            {
                public sealed class Bat(EntitySpawn spawn) : Entity(spawn);
            }

            namespace Game.Entities.Bosses
            {
                public sealed class Bat(EntitySpawn spawn) : Entity(spawn);
            }
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleEntitiesFile);
        Assert.Contains("\"enemies/bat\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"bosses/bat\"", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Game.Scenes", "Room01", "scenes/room-01")]
    [InlineData("Game.Scenes.Stage1", "Room01", "scenes/stage-1/room-01")]
    [InlineData("Game.Scenes.Room01", "Room01", "scenes/room-01")]
    [InlineData("Game.Levels", "Room01", "levels/room-01")]
    public void AScene_ClaimsTheKeyItsNamespaceNames(string space, string type, string key)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", $$"""
            using Capsule.Scenes;

            namespace {{space}};

            public sealed class {{type}}(SceneContent content) : Scene(content);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains(
            $"\"{key}\"",
            GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleScenesFile),
            StringComparison.Ordinal);
    }

    // Every kind drops any of the domain segments, so a class filed under another kind's
    // folder claims the same key as one filed under its own, and the pair is refused naming both.
    [Theory]
    [InlineData("public sealed class Bat(EntitySpawn spawn) : Entity(spawn);", "CAP003")]
    [InlineData("public sealed class Bat : Camera;", "CAP031")]
    public void TwoClassesClaimingOneKeyAcrossDomainSegments_FailTheBuildNamingBoth(string declaration, string id)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileIn("Game", $$"""
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace Game.Cameras
            {
                {{declaration}}
            }

            namespace Game.Entities
            {
                {{declaration}}
            }
            """).Diagnostics;

        Diagnostic collision = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal(id, collision.Id);

        string message = collision.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Game.Cameras.Bat", message, StringComparison.Ordinal);
        Assert.Contains("Game.Entities.Bat", message, StringComparison.Ordinal);
        Assert.Contains("'bat'", message, StringComparison.Ordinal);
    }

    // A baseScene drops the other kinds' segments too, so a base filed beside the entities or cameras
    // still serves the document naming it by its unprefixed key.
    [Theory]
    [InlineData("Game.Entities")]
    [InlineData("Game.Cameras")]
    public void ABaseSceneUnderAnotherKindsSegment_ServesTheDocumentNamingItsUnprefixedKey(string space)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn(
            "Game",
            $$"""
            using Capsule.Scenes;

            namespace {{space}};

            public abstract class PlayableRoom(SceneContent content) : Scene(content);
            """,
            ("scenes/halls/hall.scene.json", """{"baseScene": "playable-room", "entities": []}"""));

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains(
            $"global::{space}.PlayableRoom",
            GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleScenesFile),
            StringComparison.Ordinal);
    }

    // The attribute names a whole key, path and all, and the namespace says nothing.
    [Fact]
    public void AnExplicitKey_OverridesTheNamespaceWhole()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", """
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace Game.Entities.Enemies;

            [SpawnType("bosses/wyrm")]
            public sealed class Bat(EntitySpawn spawn) : Entity(spawn);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains(
            "\"bosses/wyrm\"",
            GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleEntitiesFile),
            StringComparison.Ordinal);
    }

    // An override names a whole key; one that is no key names a file the build cannot write.
    [Theory]
    [InlineData("bosses//wyrm")]
    [InlineData("../wyrm")]
    [InlineData("bosses\\\\wyrm")]
    [InlineData("/wyrm")]
    [InlineData("wyrm/")]
    [InlineData("wyrm.json")]
    [InlineData("boss wyrm")]
    [InlineData("bosses/nul")]
    public void AnUnsafeExplicitSpawnType_FailsTheBuild(string spawnType)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileIn("Game", $$"""
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace Game.Entities;

            [SpawnType("{{spawnType}}")]
            public sealed class Wyrm(EntitySpawn spawn) : Entity(spawn);
            """).Diagnostics;

        Assert.Equal("CAP019", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }
}
