using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Capsule.Diagnostics;
using Capsule.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Tests.Analyzers;

public sealed class GameBoundaryAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References = LoadReferences();

    [Fact]
    public async Task Logic_rejects_external_io_concurrency_time_and_ambient_randomness()
    {
        const string source = """
            using System;
            using System.IO;
            using System.Security.Cryptography;
            using System.Threading.Tasks;

            public static class Logic
            {
                public static async Task Run()
                {
                    _ = File.Exists("save.dat");
                    Console.WriteLine(Environment.MachineName);
                    await Task.Delay(1);
                    _ = DateTime.UtcNow;
                    _ = TimeProvider.System.GetUtcNow();
                    _ = Guid.NewGuid();
                    _ = new Random();
                    _ = RandomNumberGenerator.GetInt32(100);
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.True(diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ExternalIoId) >= 2);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ConcurrencyId);
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.AmbientTimeId));
        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.AmbientRandomId));
    }

    [Fact]
    public async Task Logic_accepts_explicit_state_and_seeded_randomness()
    {
        const string source = """
            using System;

            public sealed class Logic
            {
                private readonly Random random = new(42);
                public int Tick { get; private set; }
                public int Next() { Tick++; return random.Next(); }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Empty(diagnostics);
    }

    // The console is closed to game logic, so the engine's own log is the way out; a rule change
    // that closed that too would leave a game with nothing to say anything with.
    [Fact]
    public async Task Logic_accepts_the_engines_log_where_the_console_is_forbidden()
    {
        const string source = """
            using Capsule.Diagnostics;

            public static class Logic
            {
                public static void Say() => Log.Info("something happened");
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(
            source,
            logic: true,
            extraReferences: [MetadataReference.CreateFromFile(typeof(Log).Assembly.Location)]);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Logic_accepts_an_explicit_time_provider()
    {
        const string source = """
            using System;

            public static class Logic
            {
                public static DateTimeOffset Read(TimeProvider time) => time.GetUtcNow();
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Empty(diagnostics);
    }

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

    private static async Task<ImmutableArray<Diagnostic>> Analyze(
        string source,
        bool logic = false,
        bool shell = false,
        ImmutableArray<MetadataReference> extraReferences = default)
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
            [new GameBoundaryAnalyzer()],
            new CompilationWithAnalyzersOptions(
                new AnalyzerOptions([], new DeclaredRole(logic, shell)),
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false));

        return await analyzed.GetAnalyzerDiagnosticsAsync();
    }

    private static MetadataReference EmptyAssembly(string assemblyName)
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

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        return trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("Capsule.", StringComparison.Ordinal))
            .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("MonoGame.Framework", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    }

    private sealed class DeclaredRole(bool logic, bool shell) : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions None = new Properties(false, false);

        public override AnalyzerConfigOptions GlobalOptions { get; } = new Properties(logic, shell);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => None;

        private sealed class Properties(bool logic, bool shell) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                bool enabled = key switch
                {
                    "build_property.CapsuleGameLogic" => logic,
                    "build_property.CapsuleGameShell" => shell,
                    _ => false,
                };

                value = enabled ? "true" : null;
                return enabled;
            }
        }
    }
}
