using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

/// <summary>
/// Where a type is declared is the key it claims: its namespace under the assembly's root, minus a
/// folder repeating the type's own name.
/// </summary>
/// <remarks>
/// An entity, camera, tile type or baseScene key drops a leading <c>Entities</c>, <c>Cameras</c>,
/// <c>Tiles</c> or <c>Scenes</c> segment, whichever kind the type is. A scene claims the document at its
/// namespace's path under <c>Assets/</c>: <c>Game.Scenes.Room</c> composes
/// <c>Assets/Scenes/room.scene.json</c>.
/// </remarks>
public sealed class RegistryKeyTests
{
    [Theory]
    [InlineData(false, "Game.Entities", "Player", "player")]
    [InlineData(false, "Game.Entities.Player", "Player", "player")]
    [InlineData(false, "Game.Entities.Enemies", "Bat", "enemies/bat")]
    [InlineData(false, "Game.Entities.Enemies.Cave", "Bat", "enemies/cave/bat")]
    [InlineData(false, "Game", "Player", "player")]
    [InlineData(false, "Game.Cameras", "CameraStart", "camera-start")]
    [InlineData(false, "Game.Scenes.Doors", "Door", "doors/door")]
    [InlineData(false, "Game.Tiles.Hazards", "Spikes", "hazards/spikes")]
    [InlineData(false, "Vendor.Enemies", "Bat", "bat")]
    [InlineData(true, "Game.Scenes", "Room01", "scenes/room-01")]
    [InlineData(true, "Game.Scenes.Stage1", "Room01", "scenes/stage-1/room-01")]
    [InlineData(true, "Game.Scenes.Room01", "Room01", "scenes/room-01")]
    [InlineData(true, "Game.Levels", "Room01", "levels/room-01")]
    public void AnEntityOrScene_ClaimsTheKeyItsNamespaceNames(bool scene, string space, string type, string key)
    {
        string declaration = scene
            ? $"public sealed class {type}(SceneContent content) : Scene(content);"
            : $"public sealed class {type}(EntitySpawn spawn) : Entity(spawn);";
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", $$"""
            using Capsule.Scenes;

            namespace {{space}};

            {{declaration}}
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains(
            $"\"{key}\"",
            GeneratorHarness.Emitted(compiled, scene ? GeneratorHarness.CapsuleScenesFile : GeneratorHarness.CapsuleEntitiesFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TwoTypesOfOneNameInDifferentFolders_AreTwoKeys()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", """
            using Capsule.Scenes;

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


    // A baseScene drops the other kinds' segments too. A base filed beside the entities or cameras
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

    // The attribute names a whole key, path and all, and the namespace says nothing. An entity and a member
    // object's subclass, a camera here, take it alike.
    [Theory]
    [InlineData("public sealed class Bat(EntitySpawn spawn) : Entity(spawn);", GeneratorHarness.CapsuleEntitiesFile)]
    [InlineData("public sealed class Bat : Camera;", GeneratorHarness.CapsuleScenesFile)]
    public void AnExplicitKey_OverridesTheNamespaceWhole(string declaration, string file)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", $$"""
            using Capsule.Scenes;

            namespace Game.Entities.Enemies;

            [TypeKey("bosses/wyrm")]
            {{declaration}}
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains("\"bosses/wyrm\"", GeneratorHarness.Emitted(compiled, file), StringComparison.Ordinal);
    }

    // An override names a whole key. One that is no key names a file the build cannot write.
    [Theory]
    [InlineData("bosses//wyrm")]
    [InlineData("../wyrm")]
    [InlineData("bosses\\\\wyrm")]
    [InlineData("/wyrm")]
    [InlineData("wyrm/")]
    [InlineData("wyrm.json")]
    [InlineData("boss wyrm")]
    [InlineData("bosses/nul")]
    public void AnUnsafeExplicitTypeKey_FailsTheBuild(string key)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileIn("Game", $$"""
            using Capsule.Scenes;

            namespace Game.Entities;

            [TypeKey("{{key}}")]
            public sealed class Wyrm(EntitySpawn spawn) : Entity(spawn);
            """).Diagnostics;

        Assert.Equal("CAP019", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }
}
