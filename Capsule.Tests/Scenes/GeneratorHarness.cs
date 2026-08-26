using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Capsule.Scenes;
using Capsule.Scenes.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The generator hosted the way the compiler hosts it, over compilations built here: a rejected
/// input is an assertion rather than a broken build, and the emitted registries are read back as
/// source. The role a project would declare in MSBuild is supplied the same way the compiler
/// supplies it, so the specs exercise the gate the generator actually branches on.
/// </summary>
internal static class GeneratorHarness
{
    internal const string GameEntitiesFile = "CapsuleGameEntities.g.cs";
    internal const string GameScenesFile = "CapsuleGameScenes.g.cs";
    internal const string GameBootFile = "CapsuleGameBoot.g.cs";

    internal const string Preamble = """
        using System.Numerics;
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;
        """;

    private static readonly ImmutableArray<MetadataReference> References = LoadReferences();

    internal static IEnumerable<Diagnostic> Errors(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                yield return diagnostic;
            }
        }
    }

    internal static string Generated(string source, string fileName) => Emitted(Compile(source).Updated, fileName);

    internal static string Emitted(Compilation compiled, string fileName)
    {
        string? emitted = Emission(compiled, fileName);
        if (emitted is null)
        {
            Assert.Fail($"'{fileName}' was not generated.");
        }

        return emitted;
    }

    /// <summary>The generated file's source, or null where the generator emitted none.</summary>
    internal static string? Emission(Compilation compiled, string fileName)
    {
        foreach (SyntaxTree tree in compiled.SyntaxTrees)
        {
            if (tree.FilePath.EndsWith(fileName, StringComparison.Ordinal))
            {
                return tree.ToString();
            }
        }

        return null;
    }

    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) Compile(string source) =>
        Run(Compiled("RegistrySpecs", source, References), Role.Logic);

    /// <summary>
    /// A game's two projects as they actually meet: the logic half is generated, emitted to
    /// metadata and referenced by the shell, so the shell's compilation reaches Capsule.Scenes and
    /// the game's registries the way a real one does — through a reference, never in its own
    /// source. Pass no logic source for a shell that references no game assembly at all.
    /// </summary>
    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileShell(string shellSource, string? logicSource = null)
    {
        ImmutableArray<MetadataReference> references = logicSource is null
            ? References
            : References.Add(LogicAssembly(logicSource));

        return Run(Compiled("ShellSpecs", shellSource, references), Role.Shell);
    }

    private enum Role
    {
        Logic,
        Shell,
    }

    private static MetadataReference LogicAssembly(string source)
    {
        Compilation logic = Run(Compiled("GameSpecs", source, References), Role.Logic).Updated;

        using MemoryStream image = new();
        EmitResult emitted = logic.Emit(image);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, Errors(emitted.Diagnostics)));

        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static CSharpCompilation Compiled(string assemblyName, string source, ImmutableArray<MetadataReference> references) =>
        CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) Run(CSharpCompilation compilation, Role role)
    {
        CSharpGeneratorDriver.Create(
                [new RegistryGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: null,
                optionsProvider: new DeclaredRole(role))
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out ImmutableArray<Diagnostic> diagnostics);

        return (diagnostics, updated);
    }

    // Whatever this test host is running against, plus Capsule.Scenes itself: the generator asks
    // the compilation for Capsule.Scenes.Entity and Capsule.Scenes.Scene, so a spec without it
    // would assert nothing.
    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (string path in trusted.Split(Path.PathSeparator))
        {
            if (path.Length > 0)
            {
                paths.Add(path);
            }
        }

        paths.Add(typeof(Entity).Assembly.Location);
        paths.Add(typeof(object).Assembly.Location);

        ImmutableArray<MetadataReference>.Builder references = ImmutableArray.CreateBuilder<MetadataReference>(paths.Count);
        foreach (string path in paths)
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references.ToImmutable();
    }

    /// <summary>One declared MSBuild role, as the compiler hands it to a generator.</summary>
    private sealed class DeclaredRole : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions None = new Properties(null);

        internal DeclaredRole(Role role) =>
            GlobalOptions = new Properties(role == Role.Shell ? "build_property.CapsuleGameShell" : "build_property.CapsuleGameLogic");

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => None;

        private sealed class Properties(string? declared) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                if (declared is not null && string.Equals(key, declared, StringComparison.Ordinal))
                {
                    value = "true";

                    return true;
                }

                value = null;

                return false;
            }
        }
    }
}
