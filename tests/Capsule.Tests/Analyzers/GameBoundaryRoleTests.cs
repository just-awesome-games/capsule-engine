using System.Collections.Immutable;
using Capsule.Generators;
using Microsoft.CodeAnalysis;
using static Capsule.Tests.Analyzers.GameBoundaryFixtures;

namespace Capsule.Tests.Analyzers;

public sealed class GameBoundaryRoleTests
{
    [Fact]
    public async Task Unassigned_library_is_not_subject_to_game_role_policy()
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
    public async Task Logic_rejects_runtime_and_platform_references()
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze(
            "public static class Logic { }",
            logic: true,
            extraReferences: [EmptyAssembly("Capsule.Runtime"), EmptyAssembly("MonoGame.Framework")]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.RuntimeBoundaryId);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.PlatformBoundaryId);
    }

    [Fact]
    public async Task Shell_accepts_runtime_but_rejects_platform_reference()
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze(
            "public static class Program { }",
            shell: true,
            extraReferences: [EmptyAssembly("Capsule.Runtime"), EmptyAssembly("MonoGame.Framework.DesktopGL")]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.RuntimeBoundaryId);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.PlatformBoundaryId);
    }
}
