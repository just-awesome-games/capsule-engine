using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class GeneratorConfigurationTests
{
    private const string Source = "namespace Game; public sealed class Marker;";

    // Both roles in one project, a logic role missing the scenes it builds on, a shell role missing the
    // runtime it boots, and a shell referencing no logic assembly each fail the build with their own error.
    [Theory]
    [InlineData(true, true, new string[0], "CAP011")]
    [InlineData(true, false, new[] { "Capsule.Scenes", "Capsule.Runtime" }, "CAP012")]
    [InlineData(false, true, new[] { "Capsule.Runtime" }, "CAP013")]
    [InlineData(false, true, new string[0], "CAP015")]
    public void AMisconfiguredProject_FailsTheBuild(bool logic, bool shell, string[] excludedAssemblies, string id)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileWithRoles(Source, logic, shell, excludedAssemblies).Diagnostics;

        Assert.Equal(id, Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }
}
