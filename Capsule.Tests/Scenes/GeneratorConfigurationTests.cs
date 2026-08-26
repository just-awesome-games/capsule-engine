using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

public sealed class GeneratorConfigurationTests
{
    private const string Source = "namespace Game; public sealed class Marker;";

    [Fact]
    public void OneProjectDeclaringBothRoles_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileWithRoles(Source, logic: true, shell: true).Diagnostics;

        Assert.Equal("CAP011", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void LogicRoleWithoutScenes_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileWithRoles(
            Source,
            logic: true,
            shell: false,
            excludedAssemblies: ["Capsule.Scenes", "Capsule.Runtime"]).Diagnostics;

        Assert.Equal("CAP012", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void ShellRoleWithoutRuntime_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileWithRoles(
            Source,
            logic: false,
            shell: true,
            excludedAssemblies: ["Capsule.Runtime"]).Diagnostics;

        Assert.Equal("CAP013", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }
}
