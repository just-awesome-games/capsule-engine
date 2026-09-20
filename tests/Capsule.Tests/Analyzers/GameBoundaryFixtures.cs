using System.Collections.Immutable;
using Capsule.Diagnostics;
using Capsule.Generators;
using Capsule.Scenes;
using Capsule.Tests.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Tests.Analyzers;

internal static class GameBoundaryFixtures
{
    // The boundary rules are about what a game-logic project references, so a case declares its
    // own: nothing Capsule and nothing MonoGame is in scope until a test puts it there.
    internal static readonly ImmutableArray<MetadataReference> References = GeneratorHarness.Referenced(
        static name => name.StartsWith("Capsule.", StringComparison.Ordinal)
            || name.StartsWith("MonoGame.Framework", StringComparison.Ordinal));

    internal static async Task<ImmutableArray<Diagnostic>> Analyze(
        string source,
        bool logic = false,
        bool shell = false,
        ImmutableArray<MetadataReference> extraReferences = default,
        DiagnosticAnalyzer? analyzer = null)
    {
        ImmutableArray<MetadataReference> references = extraReferences.IsDefaultOrEmpty
            ? References
            : References.AddRange(extraReferences);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerSpecs",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers analyzed = compilation.WithAnalyzers(
            [analyzer ?? new GameBoundaryAnalyzer()],
            new CompilationWithAnalyzersOptions(
                new AnalyzerOptions([], new GeneratorHarness.DeclaredRole(logic, shell)),
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false));

        return await analyzed.GetAnalyzerDiagnosticsAsync();
    }

    internal static MetadataReference EmptyAssembly(string assemblyName)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText("internal static class Marker { }")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream image = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(image);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }
}
