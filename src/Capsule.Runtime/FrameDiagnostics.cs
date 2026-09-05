using System.Diagnostics;
using System.Globalization;

namespace Capsule.Runtime;

// Host timing capture written as one CSV: a boot trace of the stages between process start and the
// first submitted frame, then a row per frame. Owned by the host and reached only through
// WithFrameDiagnostics; a game's logic assembly never sees it.
internal sealed class FrameDiagnostics : IDisposable
{
    // Rows buffer between flushes so the frame path never touches the file; an ungraceful kill
    // loses at most this many frames.
    private const int FlushEvery = 300;

    // Index order is the order boot passes through them, and the order they are written.
    private static readonly string[] StageNames =
    [
        "builderEntered",
        "hostConstructed",
        "deviceReady",
        "texturesResident",
        "firstUpdate",
        "firstDraw",
    ];

    private readonly StreamWriter _writer;
    private readonly long[] _stages;
    private readonly Row[] _rows = new Row[FlushEvery];
    private readonly long _exitAfterTicks;

    private int _count;
    private long _sectionStart;
    private long _previousUpdateStart = -1;
    private long _firstDraw = -1;
    private double _intervalMs;
    private double _updateMs;

    // path: Where the CSV is written; an existing file is overwritten.
    //
    // builderEntered: The GetTimestamp taken when the builder was created, which is the trace's
    // first stage after process start.
    //
    // exitAfterSeconds: Real seconds after the first submitted frame at which the host requests
    // exit, or null to run until the game does.
    internal FrameDiagnostics(string path, long builderEntered, double? exitAfterSeconds)
    {
        _stages = [builderEntered, -1, -1, -1, -1, -1];
        _exitAfterTicks = exitAfterSeconds is { } seconds ? (long)(seconds * Stopwatch.Frequency) : 0;
        _writer = new StreamWriter(path, append: false) { AutoFlush = false };
    }

    // A boot stage, indexing StageNames. The first and last are the builder's entry and the first
    // submitted frame, which this type is told and takes for itself respectively; FirstUpdate is
    // taken by BeginUpdate.
    internal enum Stage
    {
        HostConstructed = 1,
        DeviceReady = 2,
        TexturesResident = 3,
        FirstUpdate = 4,
    }

    // Timestamps stage, keeping the first crossing of it.
    internal void Mark(Stage stage)
    {
        int index = (int)stage;
        if (_stages[index] < 0)
        {
            _stages[index] = Stopwatch.GetTimestamp();
        }
    }

    internal void BeginUpdate()
    {
        long now = Stopwatch.GetTimestamp();

        if (_previousUpdateStart < 0)
        {
            // The first frame has no predecessor to measure an interval against, and its update
            // is the trace's last stage before the draw it feeds.
            _intervalMs = 0;
            _stages[(int)Stage.FirstUpdate] = now;
        }
        else
        {
            _intervalMs = Milliseconds(now - _previousUpdateStart);
        }

        _previousUpdateStart = now;
        _sectionStart = now;
    }

    internal void EndUpdate() => _updateMs = Milliseconds(Stopwatch.GetTimestamp() - _sectionStart);

    internal void BeginDraw() => _sectionStart = Stopwatch.GetTimestamp();

    // Closes the frame's row; returns whether the run's time budget is spent.
    internal bool EndDraw()
    {
        long now = Stopwatch.GetTimestamp();
        _rows[_count++] = new Row(_intervalMs, _updateMs, Milliseconds(now - _sectionStart));

        if (_firstDraw < 0)
        {
            _firstDraw = now;
            _stages[^1] = now;

            // After the frame's own timestamps are taken: resolving the process start costs
            // milliseconds, and this way it lands on the first interval rather than the trace.
            WriteBootTrace();
        }

        if (_count == FlushEvery)
        {
            Flush();
        }

        return _exitAfterTicks > 0 && now - _firstDraw >= _exitAfterTicks;
    }

    public void Dispose()
    {
        Flush();
        _writer.Dispose();
    }

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // The Stopwatch timestamp the process began at. StartTime is local wall clock and the markers
    // are a monotonic counter, so the two are anchored once.
    private static long ProcessStartTimestamp()
    {
        using Process process = Process.GetCurrentProcess();
        double elapsedSeconds = (DateTime.Now - process.StartTime).TotalSeconds;

        return Stopwatch.GetTimestamp() - (long)(elapsedSeconds * Stopwatch.Frequency);
    }

    private void WriteBootTrace()
    {
        long processStart = ProcessStartTimestamp();

        _writer.WriteLine("# capsule boot trace: milliseconds since process start");
        for (int i = 0; i < StageNames.Length; i++)
        {
            _writer.Write("# ");
            _writer.Write(StageNames[i]);
            _writer.Write(',');
            _writer.WriteLine(Milliseconds(_stages[i] - processStart).ToString("F3", CultureInfo.InvariantCulture));
        }

        _writer.WriteLine("intervalMs,updateMs,drawMs");
        _writer.Flush();
    }

    private void Flush()
    {
        for (int i = 0; i < _count; i++)
        {
            Row row = _rows[i];
            _writer.Write(row.IntervalMs.ToString("F3", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(row.UpdateMs.ToString("F3", CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.WriteLine(row.DrawMs.ToString("F3", CultureInfo.InvariantCulture));
        }

        _count = 0;
        _writer.Flush();
    }

    private readonly record struct Row(double IntervalMs, double UpdateMs, double DrawMs);
}
