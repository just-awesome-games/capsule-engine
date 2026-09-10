using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class InputDriverGeneratorTests
{
    private const string Preamble = """
        using Capsule.Input;
        using Capsule.Scenes;
        using Capsule.Scenes.Input;

        namespace Game;
        """;

    [Fact]
    public void ADriver_IsRegisteredUnderItsClassName()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Compile($$"""
            {{Preamble}}

            public sealed class Walkthrough : IInputDriver
            {
                public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
                {
                    snapshot = DeviceSnapshot.Empty;
                    return tick < 60;
                }
            }
            """);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        Assert.Contains(
            "new global::Capsule.Scenes.Input.InputDriverRegistration(\"Walkthrough\", static () => new global::Game.Walkthrough())",
            GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleInputDriversFile),
            StringComparison.Ordinal);
    }

    // The registry constructs a driver with no arguments, so one that takes any is no driver a
    // command line can name; it reaches a run through WithInputDriver instead.
    [Fact]
    public void ADriverWithNoParameterlessConstructor_IsRegisteredUnderNoName()
    {
        Compilation updated = GeneratorHarness.Compile($$"""
            {{Preamble}}

            public sealed class Seeded(int seed) : IInputDriver
            {
                public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
                {
                    snapshot = DeviceSnapshot.Empty;
                    return tick < seed;
                }
            }
            """).Updated;

        Assert.DoesNotContain(
            "Seeded",
            GeneratorHarness.Emitted(updated, GeneratorHarness.CapsuleInputDriversFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TwoDriversOfOneClassName_FailTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.Compile("""
            using Capsule.Input;
            using Capsule.Scenes;
            using Capsule.Scenes.Input;

            namespace Game.Early
            {
                public sealed class Walkthrough : IInputDriver
                {
                    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
                    {
                        snapshot = DeviceSnapshot.Empty;
                        return false;
                    }
                }
            }

            namespace Game.Late
            {
                public sealed class Walkthrough : IInputDriver
                {
                    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
                    {
                        snapshot = DeviceSnapshot.Empty;
                        return false;
                    }
                }
            }
            """).Diagnostics;

        Assert.Equal("CAP020", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void ARoleFreeProject_GetsNoDriverRegistry()
    {
        Compilation updated = GeneratorHarness.CompileWithRoles(
            $$"""
            {{Preamble}}

            public sealed class Walkthrough : IInputDriver
            {
                public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
                {
                    snapshot = DeviceSnapshot.Empty;
                    return false;
                }
            }
            """,
            logic: false,
            shell: false).Updated;

        Assert.Null(GeneratorHarness.Emission(updated, GeneratorHarness.CapsuleInputDriversFile));
    }
}
