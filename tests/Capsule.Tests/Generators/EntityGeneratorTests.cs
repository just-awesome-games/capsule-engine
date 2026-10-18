using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class EntityGeneratorTests
{
    [Fact]
    public void ASpawnConstructor_IsTheWholeOptIn_AndTheNameIsKebabCased()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Player(EntitySpawn spawn) : Entity(spawn);

            public sealed class HealthPickup(EntitySpawn spawn) : Entity(spawn);

            public sealed class HTTPProbe(EntitySpawn spawn) : Entity(spawn);

            public sealed class Enemy2(EntitySpawn spawn) : Entity(spawn);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        // The spawn-type strings are the registry's contract with scene documents.
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleEntitiesFile);
        Assert.Contains("\"player\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"health-pickup\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"http-probe\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"enemy-2\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitTypeKey_ReplacesTheConvention()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            [TypeKey("player-spawn")]
            public sealed class Protagonist(EntitySpawn spawn) : Entity(spawn);
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.NotNull(compiled.GetTypeByMetadataName("Game.Protagonist"));
        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleEntitiesFile);
        string[] claims = generated.Split((char)10)
            .Where(line => line.Contains("\"player-spawn\"", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(claims);
        Assert.All(claims, line => Assert.Contains("Game.Protagonist", line, StringComparison.Ordinal));
        Assert.DoesNotContain("\"protagonist\"", generated, StringComparison.Ordinal);
    }

    // A public constructor taking a Vector2, for placement from code, is not a spawn constructor:
    // the one taking a spawn claims alone and the pair is no ambiguity.
    [Fact]
    public void ASpawnConstructorBesideACodePlacementConstructor_ClaimsOnce()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Player : Entity
            {
                public Player(EntitySpawn spawn) : base(spawn) { }
                public Player(Vector2 at) : base(at) { }
            }
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains("\"player\"", Emitted(compiled), StringComparison.Ordinal);
    }

    // The spawn carries the authored band and factor. A claiming constructor that does not hand it to a
    // base constructor drops them, and the build reports that constructor.
    [Fact]
    public void ASpawnConstructorThatDropsItsSpawn_FailsTheBuild()
    {
        string source = $$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Player : Entity
            {
                public Player(EntitySpawn spawn) : base(spawn.Position) { }
            }
            """;

        Diagnostic error = Assert.Single(GeneratorHarness.Errors(GeneratorHarness.Compile(source).Diagnostics));

        Assert.Equal("CAP026", error.Id);
        Assert.Contains("'Game.Player'", error.GetMessage(), StringComparison.Ordinal);

        // The constructor itself, by file and line: a cached model carries a position, never a tree.
        Assert.Equal(
            Array.FindIndex(
                source.Split('\n'),
                static line => line.Contains("public Player(EntitySpawn spawn)", StringComparison.Ordinal)),
            error.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Theory]
    [InlineData("public sealed class Player(EntitySpawn spawn) : Entity(spawn);")]
    [InlineData("public sealed class Player : Entity { public Player(EntitySpawn spawn) : base(spawn) { } }")]
    [InlineData("public sealed class Player : Entity { public Player(EntitySpawn spawn) : base(spawn with { Position = spawn.Position + Vector2.One }) { } }")]
    [InlineData("public sealed class Player : Entity { public Player(EntitySpawn spawn) : this(spawn, 3) { } private Player(EntitySpawn spawn, int hp) : base(spawn) { } }")]
    [InlineData("public abstract class Actor : Entity { protected Actor(EntitySpawn spawn) : base(spawn) { } } public sealed class Player : Actor { public Player(EntitySpawn spawn) : base(spawn) { } }")]
    public void ASpawnConstructorThatHandsItsSpawnOn_Claims(string source)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            {{source}}
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Contains("\"player\"", Emitted(compiled), StringComparison.Ordinal);
    }

    // An entity claiming nothing is held to nothing.
    [Fact]
    public void ANonClaimingEntityThatDropsASpawn_IsNotReported()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile($$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Prop : Entity
            {
                public Prop(Vector2 at) : base(at) { }
                internal Prop(EntitySpawn spawn) : base(spawn.Position) { }
            }
            """).Diagnostics;

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
    }

    private static string Emitted(Compilation compiled) => GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleEntitiesFile);
}
