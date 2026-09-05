using System.Diagnostics;
using System.Globalization;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

// The CSV is a documented contract: a commented boot trace, then a header, then a row per frame.
// Nothing here needs a window or a device.
public sealed class FrameDiagnosticsTests
{
    private static readonly string[] BootStages =
        ["builderEntered", "hostConstructed", "deviceReady", "texturesResident", "firstUpdate", "firstDraw"];

    [Fact]
    public void TheBootTrace_PrecedesTheHeaderAndNamesEveryStageOnce()
    {
        using Capture capture = new();
        capture.Frame();

        string[] lines = capture.ReadLines();
        int header = Array.IndexOf(lines, "intervalMs,updateMs,drawMs");

        Assert.InRange(header, 1, lines.Length - 1);
        Assert.All(lines[..header], line => Assert.StartsWith("# ", line, StringComparison.Ordinal));
        Assert.Equal(BootStages, lines[1..header].Select(line => line[2..line.IndexOf(',', StringComparison.Ordinal)]));
    }

    // Every stage is timestamped before the first draw, so each is a millisecond count that grew
    // over the boot: a negative or unordered one means a stage went unmarked.
    [Fact]
    public void TheBootTrace_ReportsTheStagesAsNonDecreasingMillisecondsFromProcessStart()
    {
        using Capture capture = new();
        capture.Frame();

        double[] stages = [.. capture.ReadLines()
            .Where(line => line.StartsWith("# ", StringComparison.Ordinal) && line.Contains(',', StringComparison.Ordinal))
            .Select(line => double.Parse(line[(line.IndexOf(',', StringComparison.Ordinal) + 1)..], CultureInfo.InvariantCulture))];

        Assert.Equal(BootStages.Length, stages.Length);
        Assert.Equal(stages.Order(), stages);
        Assert.All(stages, stage => Assert.InRange(stage, 0d, TimeSpan.FromHours(1).TotalMilliseconds));
    }

    // A capture shorter than the row buffer is the common one, and it must not be lost at exit.
    [Fact]
    public void Dispose_WritesTheFramesBufferedSinceTheLastFlush()
    {
        using Capture capture = new();
        for (int i = 0; i < 5; i++)
        {
            capture.Frame();
        }

        Assert.Equal(5, capture.ReadLines().Count(IsRow));
    }

    [Fact]
    public void EndDraw_NeverReportsABudgetSpentWithoutOne()
    {
        using Capture capture = new(exitAfterSeconds: null);

        Assert.All(Enumerable.Range(0, 10), _ => Assert.False(capture.Frame()));
    }

    [Fact]
    public void EndDraw_ReportsTheBudgetSpentOnceTheDurationHasElapsedSinceTheFirstFrame()
    {
        using Capture capture = new(exitAfterSeconds: 0.05);

        Assert.False(capture.Frame());

        Stopwatch clock = Stopwatch.StartNew();
        while (!capture.Frame())
        {
            Assert.InRange(clock.Elapsed.TotalSeconds, 0d, 10d);
        }

        Assert.InRange(clock.Elapsed.TotalSeconds, 0.04d, 10d);
    }

    private static bool IsRow(string line) => line.Length > 0 && char.IsAsciiDigit(line[0]);

    /// <summary>One capture in a temp file, driven the way the host drives it.</summary>
    private sealed class Capture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("capsule-diagnostics-");
        private readonly string _path;

        private FrameDiagnostics? _diagnostics;

        internal Capture(double? exitAfterSeconds = null)
        {
            _path = Path.Combine(_directory.FullName, "frames.csv");
            _diagnostics = new FrameDiagnostics(_path, Stopwatch.GetTimestamp(), exitAfterSeconds);

            // The host marks each of these before it submits a frame; the rest are taken here.
            _diagnostics.Mark(FrameDiagnostics.Stage.HostConstructed);
            _diagnostics.Mark(FrameDiagnostics.Stage.DeviceReady);
            _diagnostics.Mark(FrameDiagnostics.Stage.TexturesResident);
        }

        /// <summary>Runs one frame; returns whether the run's time budget is spent.</summary>
        internal bool Frame()
        {
            FrameDiagnostics diagnostics = _diagnostics ?? throw new InvalidOperationException("The capture is closed.");

            diagnostics.BeginUpdate();
            diagnostics.EndUpdate();
            diagnostics.BeginDraw();

            return diagnostics.EndDraw();
        }

        internal void Close()
        {
            _diagnostics?.Dispose();
            _diagnostics = null;
        }

        /// <summary>Ends the capture and reads what it wrote; the writer holds the file open.</summary>
        internal string[] ReadLines()
        {
            Close();

            return File.ReadAllLines(_path);
        }

        public void Dispose()
        {
            Close();
            _directory.Delete(recursive: true);
        }
    }
}
