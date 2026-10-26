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
    public async Task Logic_RejectsARuntimeReference()
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze(
            "public static class Logic { }",
            logic: true,
            extraReferences: [EmptyAssembly("Capsule.Runtime")]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CAP100");
    }

    [Fact]
    public async Task Shell_AcceptsARuntimeReference()
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze(
            "public static class Program { }",
            shell: true,
            extraReferences: [EmptyAssembly("Capsule.Runtime")]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CAP100");
    }
}
