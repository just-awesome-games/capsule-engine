using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Capsule.Build.Registry;
using Capsule.Build.Scenes;
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

    internal static IEnumerable<Diagnostic> Errors(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

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
        Run(Created("RegistrySpecs", source, References), logic: true, shell: false);

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

        return Run(Created("ShellSpecs", shellSource, references), logic: false, shell: true);
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

        return Run(Created("ShellSpecs", shellSource, references.ToImmutable()), logic: false, shell: true);
    }

    /// <summary>Compiles game code against those assets, so it names what the registry declares.</summary>
    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileAgainstSources(
        string source,
        bool logic,
        params (string Path, string? Content)[] assets) =>
        Run(Created("AssetSpecs", source, References, Documents(assets)), logic, shell: !logic);

    /// <summary>The generated registry as the game runs it, so a member hands back what it declares.</summary>
    internal static Assembly Loaded(Compilation compiled)
    {
        using MemoryStream image = new();
        EmitResult emitted = compiled.Emit(image);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, Errors(emitted.Diagnostics)));

        return Assembly.Load(image.ToArray());
    }

    // Each '<key>.scene.json' document, its path under Assets/, as the build hands it to the
    // generator: a key member marked with the build's own attribute, carrying what its parser read. A
    // '.png', '.wav' or '.ogg' path is a texture or sound member marked with the key a document names it by.
    private static string Documents((string Path, string? Content)[] documents)
    {
        const string Extension = ".scene.json";

        IEnumerable<string> members = documents
            .Select(static (document, index) =>
            {
                if (!document.Path.EndsWith(Extension, StringComparison.Ordinal))
                {
                    string name = document.Path[..Math.Max(0, document.Path.LastIndexOf('.'))];
                    string extension = document.Path[name.Length..];

                    return extension switch
                    {
                        ".png" => $"[{GeneratedAttributes.AssetName}(\"{document.Path}\")] public static global::Capsule.Assets.TextureHandle Asset{index} => new(\"{name}\", \"{extension}\");",
                        ".wav" or ".ogg" => $"[{GeneratedAttributes.AssetName}(\"{document.Path}\")] public static global::Capsule.Audio.AudioClip Asset{index} => new(\"{name}\", \"{extension}\", 0.5D);",
                        _ => string.Empty,
                    };
                }

                string key = document.Path[..^Extension.Length];
                IEnumerable<string> attributes = document.Content is null
                    ? [$"{GeneratedAttributes.SceneDocumentName}(Key = \"{key}\")"]
                    : SceneMembers.Attributes(SceneDocumentFile.Parse(document.Content), key);

                return $"{string.Concat(attributes.Select(static attribute => $"[{attribute}]"))} public static global::Capsule.Scenes.SceneKey Document{index} => new(\"{key}\");";
            });

        return $"namespace Capsule.Generated\n{{\npublic static class Documents\n{{\n{string.Join('\n', members)}\n}}\n{GeneratedAttributes.SceneDocument}{GeneratedAttributes.Asset}}}\n";
    }

    /// <summary>
    /// Runs the generator over <paramref name="before"/>, then over the same compilation with the game's
    /// source replaced by <paramref name="after"/>, with every step tracked, as the compiler does between keystrokes.
    /// </summary>
    internal static GeneratorDriverRunResult RanTwice(string before, string after, params (string Path, string? Content)[] assets)
    {
        CSharpCompilation first = Created("CachingSpecs", before, References, Documents(assets));
        CSharpCompilation second = first.ReplaceSyntaxTree(first.SyntaxTrees[0], CSharpSyntaxTree.ParseText(after));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new RegistryGenerator().AsSourceGenerator()],
            parseOptions: null,
            optionsProvider: new DeclaredRole(logic: true, shell: false),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        return driver.RunGenerators(first).RunGenerators(second).GetRunResult();
    }

    /// <summary>Compiles <paramref name="source"/> in an assembly declaring that root namespace.</summary>
    internal static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) CompileIn(
        string rootNamespace,
        string source,
        params (string Path, string? Content)[] assets) =>
        Run(
            Created("KeySpecs", source, References, assets.Length == 0 ? null : Documents(assets)),
            logic: true,
            shell: false,
            rootNamespace: rootNamespace);

    private static MetadataReference LogicAssembly(string assemblyName, string source) =>
        MetadataReference.CreateFromImage(LogicImage(assemblyName, source));

    private static byte[] LogicImage(string assemblyName, string source, params (string Path, string? Content)[] assets)
    {
        Compilation logic = Run(Created(assemblyName, source, References, assets.Length == 0 ? null : Documents(assets)), logic: true, shell: false).Updated;

        using MemoryStream image = new();
        EmitResult emitted = logic.Emit(image);

        Assert.True(emitted.Success, string.Join(Environment.NewLine, Errors(emitted.Diagnostics)));

        return image.ToArray();
    }

    /// <summary>The shell as it runs beside its one logic assembly, so its CapsuleBoot builds real registries. Disposing unloads both.</summary>
    internal static ShellContext LoadedShell(string shellSource, string logicSource, params (string Path, string? Content)[] assets)
    {
        byte[] logic = LogicImage("GameSpecs", logicSource, assets);
        Compilation shell = Run(Created("ShellSpecs", shellSource, References.Add(MetadataReference.CreateFromImage(logic))), logic: false, shell: true).Updated;

        using MemoryStream image = new();
        EmitResult emitted = shell.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, Errors(emitted.Diagnostics)));

        return new ShellContext(logic, image.ToArray());
    }

    // Resolves the shell's logic assembly from its image. The engine's assemblies fall through to the test host's.
    internal sealed class ShellContext(byte[] logic, byte[] shell) : System.Runtime.Loader.AssemblyLoadContext(isCollectible: true), IDisposable
    {
        private Assembly? _logic;

        internal Assembly Shell => field ??= LoadFromStream(new MemoryStream(shell));

        public void Dispose() => Unload();

        protected override Assembly? Load(AssemblyName assemblyName) =>
            assemblyName.Name == "GameSpecs" ? _logic ??= LoadFromStream(new MemoryStream(logic)) : null;
    }

    private static CSharpCompilation Created(
        string assemblyName,
        string source,
        ImmutableArray<MetadataReference> references,
        string? documents = null) =>
        CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source), .. documents is null ? [] : new[] { CSharpSyntaxTree.ParseText(documents) }],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) Run(
        CSharpCompilation compilation,
        bool logic,
        bool shell,
        string? rootNamespace = null)
    {
        CSharpGeneratorDriver.Create(
                [new RegistryGenerator().AsSourceGenerator()],
                parseOptions: null,
                optionsProvider: new DeclaredRole(logic, shell, rootNamespace))
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out ImmutableArray<Diagnostic> diagnostics);

        return (diagnostics, updated);
    }

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

    /// <summary>The roles a project declares, as the compiler hands them to a generator or analyzer.</summary>
    internal sealed class DeclaredRole : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions None = new Properties(logic: false, shell: false, rootNamespace: null);

        internal DeclaredRole(bool logic, bool shell, string? rootNamespace = null) =>
            GlobalOptions = new Properties(logic, shell, rootNamespace);

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => None;

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
    }
}
