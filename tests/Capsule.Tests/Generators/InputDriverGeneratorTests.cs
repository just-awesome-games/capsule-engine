using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class InputDriverGeneratorTests
{
    private const string Preamble = """
        using Capsule.Input;
        using Capsule.Scenes;

        namespace Game;
        """;

    [Fact]
    public void ADriver_IsRegisteredUnderItsClassName()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Compile($$"""
            {{Preamble}}

            {{GeneratorHarness.Walkthrough}}
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        GeneratorHarness.AssertPairs(
            GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleInputDriversFile),
            "Walkthrough",
            "Game.Walkthrough");
    }

    [Fact]
    public void ARoleFreeProject_GetsNoDriverRegistry()
    {
        Compilation updated = GeneratorHarness.CompileWithRoles(
            $$"""
            {{Preamble}}

            {{GeneratorHarness.Walkthrough}}
            """,
            logic: false,
            shell: false).Updated;

        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.CapsuleInputDriversFile));
    }
}
