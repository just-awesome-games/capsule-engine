using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class ScenePayloadGeneratorTests
{
    private const string Preamble = """
        using Capsule.Scenes;

        namespace Game;
        """;

    // A preset inherits the interface from a base payload whose fields it fixes.
    [Fact]
    public void APresetOfABasePayload_IsRegisteredUnderItsClassName()
    {
        Compilation updated = GeneratorHarness.Compile($$"""
            {{Preamble}}

            public record Arrival(string Entrance) : IScenePayload;

            public sealed record WestArrival() : Arrival("west");
            """).Updated;

        GeneratorHarness.AssertPairs(
            GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleScenePayloadsFile),
            "WestArrival",
            "Game.WestArrival");
    }

    // The registry constructs a payload with no arguments. A type it cannot construct that way is
    // still a payload in code, and no command line names it.
    [Theory]
    [InlineData("public record Arrival(string Entrance) : IScenePayload;")]
    [InlineData("public abstract class Arrival : IScenePayload;")]
    [InlineData("public sealed class Arrival<T> : IScenePayload;")]
    public void APayloadTheRegistryCannotConstruct_IsRegisteredUnderNoName(string declaration)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Compile($$"""
            {{Preamble}}

            {{declaration}}
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.DoesNotContain(
            "Arrival",
            GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleScenePayloadsFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPayloadsOfOneClassName_FailTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile("""
            using Capsule.Scenes;

            namespace Game.Early
            {
                public sealed class Checkpoint : IScenePayload;
            }

            namespace Game.Late
            {
                public sealed class Checkpoint : IScenePayload;
            }
            """).Diagnostics;

        Assert.Equal("CAP042", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }
}
