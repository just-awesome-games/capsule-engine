using System.Collections.Immutable;
using Capsule.Diagnostics;
using Capsule.Generators;
using Microsoft.CodeAnalysis;
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
}
