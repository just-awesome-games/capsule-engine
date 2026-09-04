using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

/// <summary>
/// Where a type is declared is the key it claims: its namespace under the assembly's root, minus
/// the <c>Entities</c> or <c>Scenes</c> segment that only says which registry it is in, and minus a
/// folder repeating the type's own name.
/// </summary>
public sealed class RegistryKeyTests
{
    [Theory]
    [InlineData("Game.Entities", "Player", "player")]
    [InlineData("Game.Entities.Player", "Player", "player")]
    [InlineData("Game.Entities.Enemies", "Bat", "enemies/bat")]
    [InlineData("Game.Entities.Enemies.Cave", "Bat", "enemies/cave/bat")]
    [InlineData("Game", "Player", "player")]
    public void AnEntity_ClaimsTheKeyItsNamespaceNames(string space, string type, string key)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", $$"""
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace {{space}};

            public sealed class {{type}}(EntitySpawn spawn) : Entity(spawn.Position);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains(
            $"\"{key}\"",
            GeneratorHarness.Emitted(compiled, GeneratorHarness.GameEntitiesFile),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Game.Scenes", "Room01", "room-01")]
    [InlineData("Game.Scenes.Stage1", "Room01", "stage-1/room-01")]
    [InlineData("Game.Scenes.Room01", "Room01", "room-01")]
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
            GeneratorHarness.Emitted(compiled, GeneratorHarness.GameScenesFile),
            StringComparison.Ordinal);
    }

    // A type filed outside the root namespace has no path to measure, so it claims its name alone.
    [Fact]
    public void ATypeOutsideTheRootNamespace_ClaimsItsNameAlone()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", """
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace Vendor.Enemies;

            public sealed class Bat(EntitySpawn spawn) : Entity(spawn.Position);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.GameEntitiesFile);
        Assert.Contains("\"bat\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"enemies/bat\"", generated, StringComparison.Ordinal);
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
            public sealed class Bat(EntitySpawn spawn) : Entity(spawn.Position);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains(
            "\"bosses/wyrm\"",
            GeneratorHarness.Emitted(compiled, GeneratorHarness.GameEntitiesFile),
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
            public sealed class Wyrm(EntitySpawn spawn) : Entity(spawn.Position);
            """).Diagnostics;

        Assert.Equal("CAP019", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void AnExplicitSceneKey_MayCarryAPath()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", """
            using Capsule.Scenes;

            namespace Game;

            [SceneDocument("stage-1/room-01")]
            public sealed class Opening(SceneContent content) : Scene(content);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains(
            "\"stage-1/room-01\"",
            GeneratorHarness.Emitted(compiled, GeneratorHarness.GameScenesFile),
            StringComparison.Ordinal);
    }

    // Two types in different folders no longer collide, which is the point of keying by path.
    [Fact]
    public void TwoTypesOfOneNameInDifferentFolders_AreTwoKeys()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileIn("Game", """
            using Capsule.Scenes;
            using Capsule.Scenes.Spawning;

            namespace Game.Entities.Enemies
            {
                public sealed class Bat(EntitySpawn spawn) : Entity(spawn.Position);
            }

            namespace Game.Entities.Bosses
            {
                public sealed class Bat(EntitySpawn spawn) : Entity(spawn.Position);
            }
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.GameEntitiesFile);
        Assert.Contains("\"enemies/bat\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"bosses/bat\"", generated, StringComparison.Ordinal);
    }
}
