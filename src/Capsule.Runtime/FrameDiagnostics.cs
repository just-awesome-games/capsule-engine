using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Capsule.Runtime;

// Host timing capture written as one CSV: a boot trace of the stages between process start and the
// first submitted frame, then a row per frame carrying its interval, update and draw milliseconds, the
// fixed steps its update ran and the process's gen-0 collection count as the frame ended, which lets a
// window of rows read its own collections as a difference. Owned by the host and reached through
// WithFrameDiagnostics, and a game's logic assembly never sees it.
internal sealed class FrameDiagnostics : IDisposable
{
    // Rows fill one of two buffers. A full buffer is handed to the writer thread and the other takes
    // over, so the frame path writes the file once, for the boot trace on the first frame, and never
    // waits on the disk unless the writer falls a full buffer behind. An ungraceful kill loses at most
    // the buffer being filled and the buffer being written.
    private const int FlushEvery = 300;

    // Index order is the order boot passes through them, and the order they are written.
    private static readonly string[] StageNames =
    [
        "builderEntered",
        "hostConstructed",
        "deviceReady",
        "sceneAssetsLoaded",
        "firstUpdate",
        "firstDraw",
    ];

    private readonly StreamWriter _writer;

    // One row's text, reused so writing a frame's row allocates nothing.
    private readonly char[] _row = new char[128];
    private readonly long[] _stages;
    private readonly long _exitAfterTicks;
    private readonly Func<long> _timestamp;

    // Buffers move from _free to the frame path, full to _full, and back to _free once written. One
    // writer thread drains _full in order, which is the order the frames were closed in.
    private readonly BlockingCollection<Row[]> _free = new(boundedCapacity: 2);
    private readonly BlockingCollection<Row[]> _full = new(boundedCapacity: 2);
    private readonly Thread _writerThread;
    private Row[] _rows;

    private int _count;
    private long _sectionStart;
    private long _previousUpdateStart = -1;
    private long _firstDraw = -1;
    private double _intervalMs;
    private double _updateMs;
    private int _steps;

    // path: Where the CSV is written. Its directory is created and an existing file is overwritten.
    //
    // builderEntered: The GetTimestamp taken when the builder was created, which is the trace's
    // first stage after process start.
    //
    // exitAfterSeconds: Real seconds after the first submitted frame at which the host requests
    // exit, or null to run until the game does.
    internal FrameDiagnostics(string path, long builderEntered, double? exitAfterSeconds)
        : this(path, builderEntered, exitAfterSeconds, Stopwatch.GetTimestamp)
    {
    }

    internal FrameDiagnostics(string path, long builderEntered, double? exitAfterSeconds, Func<long> timestamp)
    {
        _stages = [builderEntered, -1, -1, -1, -1, -1];
        _exitAfterTicks = exitAfterSeconds is { } seconds ? (long)(seconds * Stopwatch.Frequency) : 0;
        _timestamp = timestamp;

        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(path, append: false) { AutoFlush = false };

        _rows = new Row[FlushEvery];
        _free.Add(new Row[FlushEvery]);

        _writerThread = new Thread(WriteFullBuffers) { IsBackground = true, Name = "capsule-frame-diagnostics" };
        _writerThread.Start();
    }

    // A boot stage, indexing StageNames. The first is the builder's entry, passed in, and the last is
    // the first submitted frame, taken here. BeginUpdate takes FirstUpdate.
    internal enum Stage
    {
        HostConstructed = 1,
        DeviceReady = 2,
        SceneAssetsLoaded = 3,
        FirstUpdate = 4,
    }

    // Timestamps stage, keeping the first crossing of it.
    internal void Mark(Stage stage)
    {
        int index = (int)stage;
        if (_stages[index] < 0)
        {
            _stages[index] = _timestamp();
        }
    }

    internal void BeginUpdate()
    {
        long now = _timestamp();

        if (_previousUpdateStart < 0)
        {
            // The first frame has no predecessor to measure an interval against, and its update is the
            // trace's last stage before the draw it feeds.
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

    // steps: The fixed steps the update ran, zero on a frame that had not accumulated one.
    internal void EndUpdate(int steps)
    {
        _updateMs = Milliseconds(_timestamp() - _sectionStart);
        _steps = steps;
    }

    internal void BeginDraw() => _sectionStart = _timestamp();

    // Closes the frame's row and returns whether the run's time budget is spent.
    internal bool EndDraw()
    {
        long now = _timestamp();
        _rows[_count++] = new Row(_intervalMs, _updateMs, Milliseconds(now - _sectionStart), _steps, GC.CollectionCount(0));

        if (_firstDraw < 0)
        {
            _firstDraw = now;
            _stages[^1] = now;

            // Written after the frame's own timestamps are taken. Resolving the process start costs
            // milliseconds, which then land on the first interval and not on the trace.
            WriteBootTrace();
        }

        if (_count == FlushEvery)
        {
            _full.Add(_rows);
            _rows = _free.Take();
            _count = 0;
        }

        return _exitAfterTicks > 0 && now - _firstDraw >= _exitAfterTicks;
    }

    // Every full buffer is written before the partial one. A graceful exit loses nothing and the rows
    // stay in frame order.
    public void Dispose()
    {
        _full.CompleteAdding();
        _writerThread.Join();

        Write(_rows, _count);
        _writer.Dispose();
        _free.Dispose();
        _full.Dispose();
    }

    private void WriteFullBuffers()
    {
        foreach (Row[] rows in _full.GetConsumingEnumerable())
        {
            Write(rows, rows.Length);
            _free.Add(rows);
        }
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

    // Called on the first frame, before any row reaches the writer thread, so one thread touches the
    // writer at a time.
    private void WriteBootTrace()
    {
        long processStart = ProcessStartTimestamp();

        _writer.WriteLine("# capsule boot trace: milliseconds since process start");
        for (int i = 0; i < StageNames.Length; i++)
        {
            NumberText stage = new(_row);
            stage.Add("# ");
            stage.Add(StageNames[i]);
            stage.Add(",");
            stage.Add(Milliseconds(_stages[i] - processStart), "F3");
            _writer.WriteLine(stage.Written);
        }

        _writer.WriteLine("intervalMs,updateMs,drawMs,steps,gen0");
        _writer.Flush();
    }

    private void Write(Row[] rows, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Row row = rows[i];
            NumberText line = new(_row);
            line.Add(row.IntervalMs, "F3");
            line.Add(",");
            line.Add(row.UpdateMs, "F3");
            line.Add(",");
            line.Add(row.DrawMs, "F3");
            line.Add(",");
            line.Add(row.Steps);
            line.Add(",");
            line.Add(row.Gen0);
            _writer.WriteLine(line.Written);
        }

        _writer.Flush();
    }

    private readonly record struct Row(double IntervalMs, double UpdateMs, double DrawMs, int Steps, int Gen0);
}
