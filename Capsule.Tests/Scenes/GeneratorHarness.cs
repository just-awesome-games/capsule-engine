using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Capsule.Scenes;
using Capsule.Scenes.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Tests.Scenes;

internal static class GeneratorHarness
{
    internal const string GameEntitiesFile = "CapsuleGameEntities.g.cs";
    internal const string GameScenesFile = "CapsuleGameScenes.g.cs";
    internal const string GameAssetsFile = "CapsuleGameAssets.g.cs";
    internal const string GameBootFile = "CapsuleGameBoot.g.cs";
    internal const string RegistryProviderFile = "CapsuleRegistryProvider.g.cs";

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

    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileWithRoles(
        string source,
        bool logic,
        bool shell,
        params string[] excludedAssemblies)
    {
        ImmutableArray<MetadataReference>.Builder references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (MetadataReference reference in References)
        {
            string assemblyName = Path.GetFileNameWithoutExtension(reference.Display ?? string.Empty);
            if (!excludedAssemblies.Contains(assemblyName, StringComparer.OrdinalIgnoreCase))
            {
                references.Add(reference);
            }
        }

        return Run(Compiled("RoleSpecs", source, references.ToImmutable()), logic, shell);
    }

    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileShell(string shellSource, string? logicSource = null)
    {
        ImmutableArray<MetadataReference> references = logicSource is null
            ? References
            : References.Add(LogicAssembly("GameSpecs", logicSource));

        return Run(Compiled("ShellSpecs", shellSource, references), Role.Shell);
    }

    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileShellWithLogicAssemblies(
        string shellSource,
        params (string AssemblyName, string Source)[] logicSources)
    {
        ImmutableArray<MetadataReference>.Builder references = References.ToBuilder();
        foreach ((string assemblyName, string source) in logicSources)
        {
            references.Add(LogicAssembly(assemblyName, source));
        }

        return Run(Compiled("ShellSpecs", shellSource, references.ToImmutable()), Role.Shell);
    }

    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileWithAssets(
        bool logic,
        params string[] assetPaths)
    {
        Dictionary<string, string> domains = new(StringComparer.Ordinal);
        ImmutableArray<AdditionalText>.Builder texts = ImmutableArray.CreateBuilder<AdditionalText>(assetPaths.Length);
        foreach (string path in assetPaths)
        {
            domains[path] = path.Split('/')[0];
            texts.Add(new AssetFile(path));
        }

        return Run(
            Compiled("AssetSpecs", "namespace Game; public sealed class Marker;", References),
            logic,
            shell: !logic,
            texts.ToImmutable(),
            domains);
    }

    private enum Role
    {
        Logic,
        Shell,
    }

    private static MetadataReference LogicAssembly(string assemblyName, string source)
    {
        Compilation logic = Run(Compiled(assemblyName, source, References), Role.Logic).Updated;

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
        => Run(compilation, role == Role.Logic, role == Role.Shell);

    private static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) Run(
        CSharpCompilation compilation,
        bool logic,
        bool shell,
        ImmutableArray<AdditionalText>? assets = null,
        IReadOnlyDictionary<string, string>? domains = null)
    {
        // Both generators, as the compiler loads them: they ship in one assembly, so a spec over
        // either runs against what the other emits into the same compilation.
        CSharpGeneratorDriver.Create(
                [new RegistryGenerator().AsSourceGenerator(), new AssetRegistryGenerator().AsSourceGenerator()],
                additionalTexts: assets,
                parseOptions: null,
                optionsProvider: new DeclaredRole(logic, shell, domains))
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

    private sealed class AssetFile(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText? GetText(CancellationToken cancellationToken = default) => null;
    }

    private sealed class DeclaredRole : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions None = new Properties(logic: false, shell: false);

        private readonly IReadOnlyDictionary<string, string> _domains;

        internal DeclaredRole(bool logic, bool shell, IReadOnlyDictionary<string, string>? domains = null)
        {
            GlobalOptions = new Properties(logic, shell);
            _domains = domains ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            _domains.TryGetValue(textFile.Path, out string? domain) ? new AssetMetadata(domain) : None;

        private sealed class Properties(bool logic, bool shell) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                bool declared = logic && string.Equals(key, "build_property.CapsuleGameLogic", StringComparison.Ordinal)
                    || shell && string.Equals(key, "build_property.CapsuleGameShell", StringComparison.Ordinal);
                if (declared)
                {
                    value = "true";

                    return true;
                }

                value = null;

                return false;
            }
        }

        private sealed class AssetMetadata(string domain) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                if (string.Equals(key, "build_metadata.AdditionalFiles.CapsuleAssetDomain", StringComparison.Ordinal))
                {
                    value = domain;

                    return true;
                }

                value = null;

                return false;
            }
        }
    }
}
