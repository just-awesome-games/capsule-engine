using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

public sealed class EntityGeneratorTests
{
    [Fact]
    public void ASpawnConstructor_IsTheWholeOptIn_AndTheNameIsKebabCased()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);

            public sealed class HealthPickup(EntitySpawn spawn) : Entity(spawn.Position);

            public sealed class HTTPProbe(EntitySpawn spawn) : Entity(spawn.Position);

            public sealed class Enemy2(EntitySpawn spawn) : Entity(spawn.Position);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        // The spawn-type strings are the registry's contract with scene documents.
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.GameEntitiesFile);
        Assert.Contains("\"player\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"health-pickup\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"http-probe\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"enemy-2\"", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed class Player : Entity { public Player() : base(Vector2.Zero) { } }")]
    [InlineData("public sealed class Player : Entity { public Player(Vector2 at, int hp) : base(at) { } }")]
    [InlineData("public abstract class Hazard : Entity { protected Hazard(EntitySpawn spawn) : base(spawn.Position) { } }")]
    [InlineData("public sealed class Marker { public Marker(EntitySpawn spawn) { } }")]
    public void AClassOfAnotherShape_IsPassedOverInSilence(string declaration)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            {{declaration}}
            """);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain("Game.Player", Emitted(updated), StringComparison.Ordinal);
        Assert.DoesNotContain("Game.Marker", Emitted(updated), StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitSpawnType_ReplacesTheConvention()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [SpawnType("player-spawn")]
            public sealed class Protagonist(EntitySpawn spawn) : Entity(spawn.Position);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.NotNull(compiled.GetTypeByMetadataName("Game.Protagonist"));
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.GameEntitiesFile);
        string[] claims = generated.Split((char)10)
            .Where(line => line.Contains("\"player-spawn\"", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(claims);
        Assert.All(claims, line => Assert.Contains("Game.Protagonist", line, StringComparison.Ordinal));
        Assert.DoesNotContain("\"protagonist\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGeneratedRegistry_CompilesAndResolvesTheSpawnType()
    {
        Compilation compiled = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [SpawnType("player-spawn")]
            public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);
            """).Updated;

        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));
    }

    [Fact]
    public void TwoClassesClaimingOneType_FailTheBuildNamingBoth()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Chest(EntitySpawn spawn) : Entity(spawn.Position);

            [SpawnType("chest")]
            public sealed class IronChest(EntitySpawn spawn) : Entity(spawn.Position);
            """).Diagnostics;

        Diagnostic collision = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal("CAP003", collision.Id);

        string message = collision.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Game.Chest", message, StringComparison.Ordinal);
        Assert.Contains("Game.IronChest", message, StringComparison.Ordinal);
        Assert.Contains("'chest'", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public abstract class Hazard : Entity { protected Hazard(EntitySpawn spawn) : base(spawn.Position) { } }")]
    [InlineData("public sealed class Marker { public Marker(EntitySpawn spawn) { } }")]
    public void AClaimedTypeOnSomethingThatIsNotAConcreteEntity_FailsTheBuild(string declaration)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [SpawnType("hazard")]
            {{declaration}}
            """).Diagnostics;

        Assert.Equal("CAP001", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void AClaimedTypeWithoutASpawnConstructor_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [SpawnType("player")]
            public sealed class Player : Entity
            {
                public Player() : base(Vector2.Zero)
                {
                }
            }
            """).Diagnostics;

        Assert.Equal("CAP002", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void ABlankSpawnType_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [SpawnType("  ")]
            public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);
            """).Diagnostics;

        Assert.Equal("CAP004", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void ARegisteredEntityNestedBehindPrivateAccess_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public static class Entities
            {
                private sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);
            }
            """).Diagnostics;

        Assert.Equal("CAP008", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void MoreThanOneSpawnConstructor_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Player : Entity
            {
                public Player(EntitySpawn spawn) : base(spawn.Position) { }
                public Player(in EntitySpawn spawn) : base(spawn.Position) { }
            }
            """).Diagnostics;

        Assert.Equal("CAP010", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    private static string Generated(string source) => GeneratorHarness.Generated(source, GeneratorHarness.GameEntitiesFile);

    private static string Emitted(Compilation compiled) => GeneratorHarness.Emitted(compiled, GeneratorHarness.GameEntitiesFile);
}
