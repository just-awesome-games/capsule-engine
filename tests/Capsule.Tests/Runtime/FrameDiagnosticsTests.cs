using System.Diagnostics;
using System.Globalization;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

// The CSV is a documented contract: a commented boot trace, then a header, then a row per frame.
// Nothing here needs a window or a device.
public sealed class FrameDiagnosticsTests
{
    private static readonly string[] BootStages =
        ["builderEntered", "hostConstructed", "deviceReady", "sceneAssetsLoaded", "firstUpdate", "firstDraw"];

    // Every stage is timestamped before the first draw. Each is a millisecond count that grew
    // over the boot. A negative or unordered one means a stage went unmarked.
    [Fact]
    public void TheBootTrace_PrecedesTheHeaderAndNamesEveryStageOnceInNonDecreasingMilliseconds()
    {
        using Capture capture = new();
        Assert.False(capture.Frame());

        string[] lines = capture.ReadLines();
        int header = Array.IndexOf(lines, "intervalMs,updateMs,viewMs,drawMs,steps,gen0,bytes");

        Assert.InRange(header, 1, lines.Length - 1);
        Assert.All(lines[..header], line => Assert.StartsWith("# ", line, StringComparison.Ordinal));

        string[][] stages = [.. lines[1..header].Select(line => line[2..].Split(','))];
        double[] milliseconds = [.. stages.Select(stage => double.Parse(stage[1], CultureInfo.InvariantCulture))];

        Assert.Equal(BootStages, stages.Select(stage => stage[0]));
        Assert.Equal(milliseconds.Order(), milliseconds);
        Assert.All(milliseconds, stage => Assert.InRange(stage, 0d, TimeSpan.FromHours(1).TotalMilliseconds));
    }

    // `--frames artifacts/run.csv` on a fresh clone names a directory nothing has made yet, as a
    // frame capture's path may.
    [Fact]
    public void ACapture_CreatesTheDirectoryItsPathNames()
    {
        using TempWorkspace workspace = new("capsule-diagnostics-");
        string path = Path.Combine(workspace.Root, "nested", "deeper", "frames.csv");

        using (FrameDiagnostics diagnostics = new(path, Stopwatch.GetTimestamp(), exitAfterSeconds: null))
        {
            diagnostics.BeginUpdate();
            diagnostics.EndUpdate(steps: 1);
            diagnostics.BeginView();
            diagnostics.BeginDraw();
            diagnostics.EndDraw();
        }

        Assert.True(File.Exists(path));
    }

    // Full buffers are written by a thread the frame never waits on and the last, partial one at
    // exit. Every frame must come out, in frame order, with the steps it ran and the collection
    // count so far, which never goes down.
    [Fact]
    public void Dispose_WritesEveryFrameInOrder()
    {
        const int frames = 650;

        using Capture capture = new();
        for (int i = 0; i < frames; i++)
        {
            capture.Frame(steps: i % 5);
        }

        string[] rows = capture.ReadLines().Where(IsRow).ToArray();

        Assert.Equal(frames, rows.Length);
        Assert.Equal(Enumerable.Range(0, frames).Select(i => i % 5), rows.Select(row => int.Parse(row.Split(',')[4], CultureInfo.InvariantCulture)));
        Assert.All(rows, row => Assert.Equal(7, row.Split(',').Length));

        int[] gen0 = rows.Select(row => int.Parse(row.Split(',')[5], CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(gen0.Order(), gen0);
        Assert.InRange(gen0[0], 0, int.MaxValue);
    }

    // A row times the update, the view's build and the submission apart. Its interval and its bytes
    // both run from the previous frame's start, and the first frame has no predecessor for either.
    [Fact]
    public void ARow_TimesEachSectionApartAndCountsTheBytesAllocatedOverItsInterval()
    {
        ManualClock clock = new();
        using Capture capture = new(clock: clock);

        capture.Frame(updateSeconds: 0.001, viewSeconds: 0.002, drawSeconds: 0.004);
        byte[] allocated = new byte[100_000];
        capture.Frame();
        GC.KeepAlive(allocated);

        string[][] rows = [.. capture.ReadLines().Where(IsRow).Select(static row => row.Split(','))];

        Assert.Equal(["0.000", "1.000", "2.000", "4.000"], rows[0][..4]);
        Assert.Equal("0", rows[0][6]);
        Assert.Equal("7.000", rows[1][0]);
        Assert.InRange(long.Parse(rows[1][6], CultureInfo.InvariantCulture), allocated.Length, long.MaxValue);
    }

    [Fact]
    public void EndDraw_ReportsTheBudgetSpentOnceTheDurationHasElapsedSinceTheFirstFrame()
    {
        ManualClock clock = new();
        using Capture capture = new(exitAfterSeconds: 0.05, clock);

        Assert.False(capture.Frame());
        clock.Advance(0.049);
        Assert.False(capture.Frame());
        clock.Advance(0.001);
        Assert.True(capture.Frame());
    }

    // Regression: a duration under one timer tick truncated to zero ticks, which read as no budget.
    [Fact]
    public void EndDraw_ReportsABudgetShorterThanOneTimerTickOnceATickHasPassed()
    {
        ManualClock clock = new();
        using Capture capture = new(exitAfterSeconds: 1e-12, clock);

        Assert.False(capture.Frame());
        clock.Advance(2.0 / Stopwatch.Frequency);
        Assert.True(capture.Frame());
    }

    private static bool IsRow(string line) => line.Length > 0 && char.IsAsciiDigit(line[0]);

    /// <summary>One capture in a temp file, driven the way the host drives it.</summary>
    private sealed class Capture : IDisposable
    {
        private readonly TempWorkspace _workspace = new("capsule-diagnostics-");
        private readonly string _path;
        private readonly ManualClock? _clock;

        private FrameDiagnostics? _diagnostics;

        internal Capture(double? exitAfterSeconds = null, ManualClock? clock = null)
        {
            _path = _workspace.PathTo("frames.csv");
            _clock = clock;
            _diagnostics = clock is null
                ? new FrameDiagnostics(_path, Stopwatch.GetTimestamp(), exitAfterSeconds)
                : new FrameDiagnostics(_path, clock.Timestamp, exitAfterSeconds, clock.GetTimestamp);

            // The host marks each of these before it submits a frame. The rest are taken here.
            _diagnostics.Mark(FrameDiagnostics.Stage.HostConstructed);
            _diagnostics.Mark(FrameDiagnostics.Stage.DeviceReady);
            _diagnostics.Mark(FrameDiagnostics.Stage.SceneAssetsLoaded);
        }

        /// <summary>
        /// Runs one frame that stepped <paramref name="steps"/> times, spending the given seconds of a manual clock in
        /// each section, and returns whether the run's time budget is spent.
        /// </summary>
        internal bool Frame(int steps = 1, double updateSeconds = 0, double viewSeconds = 0, double drawSeconds = 0)
        {
            FrameDiagnostics diagnostics = _diagnostics ?? throw new InvalidOperationException("The capture is closed.");

            diagnostics.BeginUpdate();
            _clock?.Advance(updateSeconds);
            diagnostics.EndUpdate(steps);
            diagnostics.BeginView();
            _clock?.Advance(viewSeconds);
            diagnostics.BeginDraw();
            _clock?.Advance(drawSeconds);

            return diagnostics.EndDraw();
        }

        internal void Close()
        {
            _diagnostics?.Dispose();
            _diagnostics = null;
        }

        /// <summary>Ends the capture, which closes the file the writer held, and reads what it wrote.</summary>
        internal string[] ReadLines()
        {
            Close();

            return File.ReadAllLines(_path);
        }

        public void Dispose()
        {
            Close();
            _workspace.Dispose();
        }
    }

    private sealed class ManualClock
    {
        internal long Timestamp { get; private set; }

        internal long GetTimestamp() => Timestamp;

        internal void Advance(double seconds) =>
            Timestamp += (long)(seconds * Stopwatch.Frequency);
    }
}
