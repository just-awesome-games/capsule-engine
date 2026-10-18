using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using static Capsule.Tests.Generators.GameBoundaryFixtures;

namespace Capsule.Tests.Generators;

public sealed class GameBoundaryRoleTests
{
    [Fact]
    public async Task AnUnassignedLibrary_IsNotSubjectToGameRolePolicy()
    {
        const string source = """
            using System;
            using System.IO;
            public static class Tool { public static string Read() => File.ReadAllText("tool.txt") + DateTime.Now; }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: false, shell: false);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Logic_RejectsRuntimeAndPlatformReferences()
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze(
            "public static class Logic { }",
            logic: true,
            extraReferences: [EmptyAssembly("Capsule.Runtime"), EmptyAssembly("MonoGame.Framework")]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CAP100");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CAP101");
    }

    [Fact]
    public async Task Shell_AcceptsRuntimeButRejectsAPlatformReference()
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze(
            "public static class Program { }",
            shell: true,
            extraReferences: [EmptyAssembly("Capsule.Runtime"), EmptyAssembly("MonoGame.Framework.DesktopGL")]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CAP100");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CAP101");
    }
}
