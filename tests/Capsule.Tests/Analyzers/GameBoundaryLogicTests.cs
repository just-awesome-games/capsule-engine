using System.Collections.Immutable;
using Capsule.Diagnostics;
using Capsule.Generators;
using Capsule.Tests.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Capsule.Tests.Analyzers.GameBoundaryFixtures;

namespace Capsule.Tests.Analyzers;

public sealed class GameBoundaryLogicTests
{
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

    [Fact]
    public async Task Logic_rejects_locks_native_imports_and_property_based_io()
    {
        const string source = """
            using System.IO;
            using System.Runtime.InteropServices;
            using System.Threading;

            public static partial class Logic
            {
                private static readonly object Gate = new();

                [DllImport("native")]
                private static extern int ReadNative();

                [LibraryImport("native")]
                private static partial int ReadGenerated();

                public static long Read(FileInfo file)
                {
                    lock (Gate) { }
                    Monitor.Enter(Gate);
                    return file.Length;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ExternalIoId));
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ConcurrencyId));
    }

    [Fact]
    public async Task Logic_accepts_in_memory_io_and_non_scheduling_threading_helpers()
    {
        const string source = """
            using System.IO;
            using System.Threading;

            public static class Logic
            {
                private static int value;

                public static string Run()
                {
                    using MemoryStream stream = new();
                    stream.WriteByte(42);
                    stream.Position = 0;
                    using StringWriter writer = new();
                    writer.Write(Path.Combine("maps", "room"));
                    Interlocked.Increment(ref value);
                    CancellationToken.None.ThrowIfCancellationRequested();
                    return writer.ToString();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Logic_classifies_path_enumeration_and_timer_operations_by_behavior()
    {
        const string source = """
            using System.IO;
            using System.IO.Enumeration;
            using System.Timers;

            public static class Logic
            {
                public static (string Path, bool Matches) Run()
                {
                    _ = Path.GetTempFileName();
                    _ = Path.GetTempPath();
                    _ = Path.GetFullPath("save.dat");
                    _ = Path.GetInvalidFileNameChars();
                    _ = Path.GetInvalidPathChars();
                    _ = Path.Exists("save.dat");
                    _ = Path.GetRandomFileName();
                    _ = Path.DirectorySeparatorChar;
                    _ = Path.AltDirectorySeparatorChar;
                    _ = Path.VolumeSeparatorChar;
                    _ = Path.PathSeparator;
                    using Timer timer = new();
                    return (
                        Path.GetFullPath("save.dat", "/game"),
                        FileSystemName.MatchesSimpleExpression("*.dat", "save.dat"));
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Equal(10, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ExternalIoId));
        Assert.Single(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ConcurrencyId);
        Assert.Single(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.AmbientRandomId);
    }

    // Sub-namespaces and members no denylist anticipated stay closed by default.
    [Fact]
    public async Task Logic_rejects_unlisted_io_namespaces_and_blocking_task_use()
    {
        const string source = """
            using System.IO.Compression;
            using System.IO.MemoryMappedFiles;
            using System.Threading.Channels;
            using System.Threading.Tasks;

            public static class Logic
            {
                public static void Run()
                {
                    ZipFile.ExtractToDirectory("pack.zip", "out");
                    _ = MemoryMappedFile.CreateFromFile("save.dat");
                    _ = Channel.CreateUnbounded<int>();
                    Task task = new(() => { });
                    task.Wait();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ExternalIoId));
        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ConcurrencyId));
    }

    // The console is closed to game logic, so the engine's own log must stay open.
    [Fact]
    public async Task Logic_accepts_the_engines_log_where_the_console_is_forbidden()
    {
        const string source = """
            using System.Numerics;
            using Capsule.Diagnostics;
            using Capsule.Rendering;

            public static class Logic
            {
                public static void Say() => Log.Info("something happened");
                public static void Show() => DebugDraw.Line("logic", Vector2.Zero, Vector2.One, ColorRgba.White);
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

    [Theory]
    [InlineData("MathF.Sin(1f)", "DeterministicMath.Sin")]
    [InlineData("Math.Atan2(1d, 2d)", "DeterministicMath.Atan2")]
    [InlineData("float.Cos(1f)", "DeterministicMath.Cos")]
    [InlineData("double.Exp2(1d)", "DeterministicMath.Exp2")]
    [InlineData("MathF.SinCos(1f)", "DeterministicMath.Sin and DeterministicMath.Cos")]
    [InlineData("MathF.Tan(1f)", "DeterministicMath.Tan")]
    [InlineData("Math.Log(8d, 2d)", "DeterministicMath.Log(a) / DeterministicMath.Log(newBase)")]
    public async Task Logic_rejects_a_platform_transcendental_that_deterministic_math_replaces(string call, string replacement)
    {
        string source = $$"""
            using System;

            public static class Logic
            {
                public static object Angle() => {{call}};
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(GameBoundaryAnalyzer.PlatformMathId, diagnostic.Id);
        Assert.EndsWith("Call " + replacement, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    // The rule names only functions with a DeterministicMath twin, so every report names its replacement.
    [Fact]
    public async Task Logic_accepts_deterministic_math_and_platform_functions_without_a_twin()
    {
        const string source = """
            using System;
            using Capsule;

            public static class Logic
            {
                public static float Angle(float x) => DeterministicMath.Sin(x) + MathF.Cbrt(x) + MathF.Sqrt(x);
            }
            """;
        MetadataReference core = MetadataReference.CreateFromFile(typeof(DeterministicMath).Assembly.Location);

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true, extraReferences: [core]);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerSpecs",
            [CSharpSyntaxTree.ParseText(source)],
            References.Add(core),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(GeneratorHarness.Errors(compilation.GetDiagnostics()));
        Assert.Empty(diagnostics);
    }
}
