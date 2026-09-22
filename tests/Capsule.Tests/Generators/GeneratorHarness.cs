using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Capsule.Generators;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tiles;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Tests.Generators;

internal static class GeneratorHarness
{
    internal const string CapsuleEntitiesFile = "CapsuleEntities.g.cs";
    internal const string CapsuleScenesFile = "CapsuleScenes.g.cs";
    internal const string CapsuleAssetsFile = "CapsuleAssets.g.cs";
    internal const string CapsuleBootFile = "CapsuleBoot.g.cs";
    internal const string CapsuleInputDriversFile = "CapsuleInputDrivers.g.cs";

    internal const string Preamble = """
        using System.Numerics;
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;
        """;

    private static readonly ImmutableArray<MetadataReference> References =
        Referenced(null, typeof(Entity).Assembly, typeof(TileGrid).Assembly, typeof(object).Assembly);

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

    /// <summary>
    /// Asserts every emitted line naming <paramref name="key"/> names <paramref name="type"/> too.
    /// A pairing, so how the line is spelled is not pinned by a test. A document's key constant names
    /// the key alone and is passed over.
    /// </summary>
    internal static void AssertPairs(string generated, string key, string type)
    {
        string[] lines = generated.Split((char)10)
            .Where(line => line.Contains($"\"{key}\"", StringComparison.Ordinal))
            .Where(static line => !line.Contains(" const string ", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Contains(type, line, StringComparison.Ordinal));
    }

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
        Run(Created("RegistrySpecs", source, References), Role.Logic);

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

        return Run(Created("RoleSpecs", source, references.ToImmutable()), logic, shell);
    }

    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileShell(string shellSource, string? logicSource = null)
    {
        ImmutableArray<MetadataReference> references = logicSource is null
            ? References
            : References.Add(LogicAssembly("GameSpecs", logicSource));

        return Run(Created("ShellSpecs", shellSource, references), Role.Shell);
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

        return Run(Created("ShellSpecs", shellSource, references.ToImmutable()), Role.Shell);
    }

    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileWithAssets(
        bool logic,
        params string[] assetPaths) =>
        CompileWithSources(logic, [.. assetPaths.Select(static path => (path, (string?)null))]);

    /// <summary>Compiles against assets the compiler can read, as it reads a font description.</summary>
    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileWithSources(
        bool logic,
        params (string Path, string? Content)[] assets) =>
        CompileAgainstSources("namespace Game; public sealed class Marker;", logic, assets);

    /// <summary>Compiles game code against those assets, so it names what the registry declares.</summary>
    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileAgainstSources(
        string source,
        bool logic,
        params (string Path, string? Content)[] assets)
    {
        (ImmutableArray<AdditionalText> texts, Dictionary<string, (string Domain, string Path, string? BaseScene, string? Camera)> declared) = Assets(assets);

        return Run(Created("AssetSpecs", source, References), logic, shell: !logic, texts, declared);
    }

    /// <summary>The generated registry as the game runs it, so a member hands back what it declares.</summary>
    internal static Assembly Loaded(Compilation compiled)
    {
        using MemoryStream image = new();
        EmitResult emitted = compiled.Emit(image);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, Errors(emitted.Diagnostics)));

        return Assembly.Load(image.ToArray());
    }

    /// <summary>Compiles game code against the assets the asset hook would hand the compiler beside it.</summary>
    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileAgainstAssets(
        string source,
        params string[] assetPaths)
    {
        (ImmutableArray<AdditionalText> texts, Dictionary<string, (string Domain, string Path, string? BaseScene, string? Camera)> assets) =
            Assets([.. assetPaths.Select(static path => (path, (string?)null))]);

        return Run(Created("ResidencySpecs", source, References), logic: true, shell: false, texts, assets);
    }

    // Each path is '<domain>/<path under the domain root>', as the asset hook hands the generator
    // beside the file.
    private static (ImmutableArray<AdditionalText> Texts, Dictionary<string, (string Domain, string Path, string? BaseScene, string? Camera)> Assets) Assets(
        (string Path, string? Content)[] assetPaths)
    {
        const string SceneDocumentExtension = ".scene.json";

        Dictionary<string, (string Domain, string Path, string? BaseScene, string? Camera)> assets = new(StringComparer.Ordinal);
        ImmutableArray<AdditionalText>.Builder texts = ImmutableArray.CreateBuilder<AdditionalText>(assetPaths.Length);
        foreach ((string path, string? content) in assetPaths)
        {
            int separator = path.IndexOf('/', StringComparison.Ordinal);
            string relative = path[(separator + 1)..];
            string domain = path[..separator];

            // A scene document's metadata is its key, the build's own derivation: the fixed
            // '.scene.json' suffix comes off whole, not just the file's last extension.
            string metadataPath = domain == "scenes" && relative.EndsWith(SceneDocumentExtension, StringComparison.Ordinal)
                ? relative[..^SceneDocumentExtension.Length]
                : StripLastExtension(relative);

            // The real build hands the generator a scene document's baseScene and camera already
            // resolved by its own parser. The harness stands in for that build, so it resolves them
            // here the same way rather than leaving the generator to read the document's text.
            (string? baseScene, string? camera) = domain == "scenes" && content is not null
                ? SceneDocumentBaseAndCamera(content)
                : (null, null);

            assets[path] = (domain, metadataPath, baseScene, camera);
            texts.Add(new AssetText(path, content));
        }

        return (texts.ToImmutable(), assets);

        static string StripLastExtension(string relative)
        {
            int dot = relative.LastIndexOf('.');

            return dot < 0 ? relative : relative[..dot];
        }
    }

    private static (string? BaseScene, string? Camera) SceneDocumentBaseAndCamera(string content)
    {
        SceneDocument document = SceneDocumentFile.Parse(content);

        return (document.Settings.BaseScene, document.Settings.Camera);
    }

    /// <summary>
    /// Runs both generators twice over one unchanged compilation, with every step tracked, which is
    /// what the compiler does between keystrokes that changed nothing a generator reads.
    /// </summary>
    internal static GeneratorDriverRunResult RanTwice(params (string Path, string? Content)[] assets)
    {
        (ImmutableArray<AdditionalText> texts, Dictionary<string, (string Domain, string Path, string? BaseScene, string? Camera)> declared) = Assets(assets);
        CSharpCompilation compilation = Created("CachingSpecs", "namespace Game; public sealed class Marker;", References);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new RegistryGenerator().AsSourceGenerator(), new AssetRegistryGenerator().AsSourceGenerator()],
            additionalTexts: texts,
            parseOptions: null,
            optionsProvider: new DeclaredRole(logic: true, shell: false, declared),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        return driver.RunGenerators(compilation).RunGenerators(compilation).GetRunResult();
    }

    /// <summary>Compiles <paramref name="source"/> in an assembly declaring that root namespace.</summary>
    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileIn(
        string rootNamespace,
        string source) =>
        Run(
            Created("KeySpecs", source, References),
            logic: true,
            shell: false,
            assets: null,
            rootNamespace: rootNamespace);

    private enum Role
    {
        Logic,
        Shell,
    }

    private static MetadataReference LogicAssembly(string assemblyName, string source)
    {
        Compilation logic = Run(Created(assemblyName, source, References), logic: true, shell: false).Updated;

        using MemoryStream image = new();
        EmitResult emitted = logic.Emit(image);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, Errors(emitted.Diagnostics)));

        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static CSharpCompilation Created(string assemblyName, string source, ImmutableArray<MetadataReference> references) =>
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
        ImmutableArray<AdditionalText>? texts = null,
        IReadOnlyDictionary<string, (string Domain, string Path, string? BaseScene, string? Camera)>? assets = null,
        string? rootNamespace = null)
    {
        // Both generators, as the compiler loads them: they ship in one assembly, so a spec over
        // either runs against what the other emits into the same compilation.
        CSharpGeneratorDriver.Create(
                [new RegistryGenerator().AsSourceGenerator(), new AssetRegistryGenerator().AsSourceGenerator()],
                additionalTexts: texts,
                parseOptions: null,
                optionsProvider: new DeclaredRole(logic, shell, assets, rootNamespace))
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out ImmutableArray<Diagnostic> diagnostics);

        return (diagnostics, updated);
    }

    /// <summary>The generated registry as a game names it, off a Probe class holding <paramref name="members"/>.</summary>
    internal static Assembly Probed(string members, params (string Path, string? Content)[] assets)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = CompileAgainstSources(
            "namespace Game;\n\npublic static class Probe\n{\n" + members + "\n}\n",
            logic: true,
            assets);

        Assert.Empty(Errors(diagnostics));

        return Loaded(compiled);
    }

    /// <summary>The generated registry as a game loads it, with nothing named off it.</summary>
    internal static Assembly Compiled(params (string Path, string? Content)[] assets) => Probed(string.Empty, assets);

    /// <summary>Why the generators refused those assets.</summary>
    internal static IEnumerable<Diagnostic> Refused(params (string Path, string? Content)[] assets) =>
        Errors(CompileWithSources(logic: true, assets).Diagnostics);

    /// <summary>
    /// Whatever this test host is running against, minus what <paramref name="excluding"/> names,
    /// plus the assemblies a case needs whether or not the host loaded them.
    /// </summary>
    internal static ImmutableArray<MetadataReference> Referenced(
        Func<string, bool>? excluding,
        params Assembly[] also)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (string path in trusted.Split(Path.PathSeparator))
        {
            if (path.Length > 0 && excluding?.Invoke(Path.GetFileNameWithoutExtension(path)) != true)
            {
                paths.Add(path);
            }
        }

        foreach (Assembly assembly in also)
        {
            paths.Add(assembly.Location);
        }

        ImmutableArray<MetadataReference>.Builder references = ImmutableArray.CreateBuilder<MetadataReference>(paths.Count);
        foreach (string path in paths)
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references.ToImmutable();
    }

    private sealed class AssetText(string path, string? content) : AdditionalText
    {
        public override string Path { get; } = path;

        // Null where the compiler could not read the file, which is every binary asset.
        public override SourceText? GetText(CancellationToken cancellationToken = default) =>
            content is null ? null : SourceText.From(content);
    }

    /// <summary>The roles a project declares, as the compiler hands them to a generator or analyzer.</summary>
    internal sealed class DeclaredRole : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions None = new Properties(logic: false, shell: false, rootNamespace: null);

        private readonly IReadOnlyDictionary<string, (string Domain, string Path, string? BaseScene, string? Camera)> _assets;

        internal DeclaredRole(
            bool logic,
            bool shell,
            IReadOnlyDictionary<string, (string Domain, string Path, string? BaseScene, string? Camera)>? assets = null,
            string? rootNamespace = null)
        {
            GlobalOptions = new Properties(logic, shell, rootNamespace);
            _assets = assets ?? new Dictionary<string, (string, string, string?, string?)>(StringComparer.Ordinal);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            _assets.TryGetValue(textFile.Path, out (string Domain, string Path, string? BaseScene, string? Camera) asset)
                ? new AssetMetadata(asset.Domain, asset.Path, asset.BaseScene, asset.Camera)
                : None;

        private sealed class Properties(bool logic, bool shell, string? rootNamespace) : AnalyzerConfigOptions
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

                if (rootNamespace is not null && string.Equals(key, "build_property.RootNamespace", StringComparison.Ordinal))
                {
                    value = rootNamespace;

                    return true;
                }

                value = null;

                return false;
            }
        }

        private sealed class AssetMetadata(string domain, string path, string? baseScene, string? camera) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                if (string.Equals(key, "build_metadata.AdditionalFiles.CapsuleAssetDomain", StringComparison.Ordinal))
                {
                    value = domain;

                    return true;
                }

                if (string.Equals(key, "build_metadata.AdditionalFiles.CapsuleAssetPath", StringComparison.Ordinal))
                {
                    value = path;

                    return true;
                }

                if (string.Equals(key, "build_metadata.AdditionalFiles.CapsuleAssetBaseScene", StringComparison.Ordinal) && baseScene is not null)
                {
                    value = baseScene;

                    return true;
                }

                if (string.Equals(key, "build_metadata.AdditionalFiles.CapsuleAssetCamera", StringComparison.Ordinal) && camera is not null)
                {
                    value = camera;

                    return true;
                }

                value = null;

                return false;
            }
        }
    }
}
