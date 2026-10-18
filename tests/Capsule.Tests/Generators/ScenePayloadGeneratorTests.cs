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
}
