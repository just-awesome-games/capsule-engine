using System.Collections.Immutable;
using Capsule.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Capsule.Tests.Generators.GameBoundaryFixtures;

namespace Capsule.Tests.Generators;

public sealed class GameBoundaryLogicTests
{
    [Fact]
    public async Task Logic_RejectsExternalIoConcurrencyTimeAndAmbientRandomness()
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

        Assert.True(diagnostics.Count(diagnostic => diagnostic.Id == "CAP102") >= 2);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CAP103");
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "CAP104"));
        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == "CAP105"));
    }

    [Fact]
    public async Task Logic_RejectsLocksNativeImportsAndPropertyBasedIo()
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

        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == "CAP102"));
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "CAP103"));
    }

    [Fact]
    public async Task Logic_ClassifiesPathEnumerationAndTimerOperationsByBehavior()
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

        Assert.Equal(10, diagnostics.Count(diagnostic => diagnostic.Id == "CAP102"));
        Assert.Single(diagnostics, diagnostic => diagnostic.Id == "CAP103");
        Assert.Single(diagnostics, diagnostic => diagnostic.Id == "CAP105");
    }

    // Sub-namespaces and members no denylist anticipated stay closed by default.
    [Fact]
    public async Task Logic_RejectsUnlistedIoNamespacesAndBlockingTaskUse()
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

        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "CAP102"));
        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == "CAP103"));
    }

    [Theory]
    [InlineData("MathF.Sin(1f)", "DeterministicMath.Sin")]
    [InlineData("Math.Atan2(1d, 2d)", "DeterministicMath.Atan2")]
    [InlineData("float.Cos(1f)", "DeterministicMath.Cos")]
    [InlineData("double.Exp2(1d)", "DeterministicMath.Exp2")]
    [InlineData("MathF.SinCos(1f)", "DeterministicMath.Sin and DeterministicMath.Cos")]
    [InlineData("MathF.Tan(1f)", "DeterministicMath.Tan")]
    [InlineData("Math.Log(8d, 2d)", "DeterministicMath.Log(a) / DeterministicMath.Log(newBase)")]
    public async Task Logic_RejectsAPlatformTranscendentalThatDeterministicMathReplaces(string call, string replacement)
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
        Assert.Equal("CAP107", diagnostic.Id);
        Assert.EndsWith("Call " + replacement, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    // Reflection is refused on its own terms, not as I/O, and the report names what to write instead.
    [Fact]
    public async Task Logic_RejectsReflectionAndNamesTheFix()
    {
        const string source = """
            public static class Logic
            {
                public static string Name(object scene) => scene.GetType().Name;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("CAP109", diagnostic.Id);
        Assert.Contains("nameof", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    // Each source compiles clean against the engine and draws no report: state the logic owns, in-memory
    // I/O and threading helpers that neither schedule nor block, the engine's log where the console is
    // forbidden, an explicit time provider, and the platform functions without a DeterministicMath twin.
    [Theory]
    [InlineData("""
        public sealed class Logic
        {
            public int Tick { get; private set; }
            public int Advance() { Tick++; return Tick; }
        }
        """)]
    [InlineData("""
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
        """)]
    [InlineData("""
        using System.Numerics;
        using Capsule.Diagnostics;
        using Capsule.Rendering;

        public static class Logic
        {
            public static void Say() => Log.Info("something happened");
            public static void Show() => DebugDraw.Line("logic", Vector2.Zero, Vector2.One, ColorRgba.White);
        }
        """)]
    [InlineData("""
        using System;

        public static class Logic
        {
            public static DateTimeOffset Read(TimeProvider time) => time.GetUtcNow();
        }
        """)]
    [InlineData("""
        using System;
        using Capsule;

        public static class Logic
        {
            public static float Angle(float x) => DeterministicMath.Sin(x) + MathF.Cbrt(x) + MathF.Sqrt(x);
        }
        """)]
    public async Task Logic_AcceptsWhatTouchesNothingOutsideTheSimulation(string source)
    {
        MetadataReference core = MetadataReference.CreateFromFile(typeof(Log).Assembly.Location);

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true, extraReferences: [core]);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AnalyzerSpecs",
            [CSharpSyntaxTree.ParseText(source)],
            References.Add(core),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(GeneratorHarness.Errors(compilation.GetDiagnostics()));
        Assert.Empty(diagnostics);
    }

    // CAP102 and CAP103 name what replaces the refused call, chosen by what the call does.
    [Theory]
    [InlineData("System.Console.WriteLine(1);", "CAP102", "Write through Capsule.Diagnostics.Log")]
    [InlineData("_ = System.IO.File.Exists(\"save.dat\");", "CAP102", "Persist through Run.Saves")]
    [InlineData("_ = System.Environment.MachineName;", "CAP102", "to the shell")]
    [InlineData("_ = System.Threading.Tasks.Task.Delay(1);", "CAP103", "Wait with a Countdown")]
    [InlineData("System.Threading.Thread.Sleep(1);", "CAP103", "Wait with a Countdown")]
    [InlineData("_ = new System.Threading.Thread(() => { });", "CAP103", "Do the work inside the step")]
    public async Task Logic_RejectsIoAndConcurrencyAndNamesTheReplacement(string statement, string id, string fix)
    {
        string source = $$"""
            public static class Logic
            {
                public static void Run() { {{statement}} }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Contains(fix, diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
