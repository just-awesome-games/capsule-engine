using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Capsule.Diagnostics;
using Capsule.Generators;
using Capsule.Scenes;
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
    public async Task Logic_accepts_explicit_state_it_owns()
    {
        const string source = """
            public sealed class Logic
            {
                public int Tick { get; private set; }
                public int Advance() { Tick++; return Tick; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Empty(diagnostics);
    }

    // CAP105 closes the ambient APIs, so the seam it leaves open has to stay open.
    [Fact]
    public async Task Logic_accepts_the_seeded_random_source_reached_through_the_scene()
    {
        const string source = """
            using Capsule.Scenes;

            public sealed class Blinker : Entity
            {
                public Blinker() : base(default) { }

                public int NextBlink() => Random.Range(30, 90);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(
            source,
            logic: true,
            extraReferences:
            [
                MetadataReference.CreateFromFile(typeof(StepContext).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Entity).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Numerics.Vector2).Assembly.Location),
            ]);

        Assert.Empty(diagnostics);
    }

    // A method group is a use the invocation hook never sees, and the delegate outlives it.
    [Fact]
    public async Task Logic_rejects_a_system_random_member_taken_as_a_method_group()
    {
        const string source = """
            using System;

            public static class Logic
            {
                public static Func<int> Escape(Random random) => random.Next;
                public static Func<int> Wrapped(Random random) => new Func<int>(random.Next);
                public static Func<int> Shared() => Random.Shared.Next;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        // Three method groups, and the Random.Shared read one of them is taken from.
        Assert.Equal(4, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.AmbientRandomId));
    }

    // System.Random's seeded sequence is not stable across runtime versions.
    [Fact]
    public async Task Logic_rejects_a_seeded_system_random_it_constructs_holds_or_draws_from()
    {
        const string source = """
            using System;

            public sealed class Logic
            {
                private readonly Random random = new(42);
                public int Next() => random.Next();
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.AmbientRandomId));
    }

    // The console is closed to game logic, so the engine's own log must stay open.
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
