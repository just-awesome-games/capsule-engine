using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Capsule.Runtime;

// Host timing capture written as the CSV EngineBuilder.WithFrameDiagnostics documents. The gen-0
// count is cumulative, and a window of rows reads its own collections as a difference. A row's
// interval and bytes cover the same span, from the previous frame's start to this one's, on the
// thread that runs the frames.
internal sealed class FrameDiagnostics : IDisposable
{
    // Rows fill one of two buffers. A full buffer is handed to the writer thread and the other takes
    // over. The frame path writes the file once, for the boot trace on the first frame, and never
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
    private long _previousAllocated;
    private double _intervalMs;
    private double _updateMs;
    private double _viewMs;
    private long _bytes;
    private int _steps;

    // builderEntered is the GetTimestamp taken when the builder was created, the trace's first stage.
    internal FrameDiagnostics(string path, long builderEntered, double? exitAfterSeconds)
        : this(path, builderEntered, exitAfterSeconds, Stopwatch.GetTimestamp)
    {
    }

    internal FrameDiagnostics(string path, long builderEntered, double? exitAfterSeconds, Func<long> timestamp)
    {
        _stages = [builderEntered, -1, -1, -1, -1, -1];
        // A positive duration under one tick still binds. Zero is no budget.
        _exitAfterTicks = exitAfterSeconds is { } seconds ? Math.Max(1L, (long)(seconds * Stopwatch.Frequency)) : 0;
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
        long allocated = GC.GetAllocatedBytesForCurrentThread();

        if (_previousUpdateStart < 0)
        {
            // The first frame has no predecessor to measure an interval against, and its update is the
            // trace's last stage before the draw it feeds.
            _intervalMs = 0;
            _bytes = 0;
            _stages[(int)Stage.FirstUpdate] = now;
        }
        else
        {
            _intervalMs = Milliseconds(now - _previousUpdateStart);
            _bytes = allocated - _previousAllocated;
        }

        _previousUpdateStart = now;
        _previousAllocated = allocated;
        _sectionStart = now;
    }

    // steps: The fixed steps the update ran, zero on a frame that had not accumulated one.
    internal void EndUpdate(int steps)
    {
        long now = _timestamp();
        _updateMs = Milliseconds(now - _sectionStart);
        _steps = steps;
        _sectionStart = now;
    }

    // Opens the frame's build, the first read of the view after a step, which BeginDraw closes.
    internal void BeginView() => _sectionStart = _timestamp();

    internal void BeginDraw()
    {
        long now = _timestamp();
        _viewMs = Milliseconds(now - _sectionStart);
        _sectionStart = now;
    }

    // Closes the frame's row and returns whether the run's time budget is spent.
    internal bool EndDraw()
    {
        long now = _timestamp();
        _rows[_count++] = new Row(_intervalMs, _updateMs, _viewMs, Milliseconds(now - _sectionStart), _steps, GC.CollectionCount(0), _bytes);

        if (_stages[^1] < 0)
        {
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

        return Spent(now);
    }

    // Whether the run's time budget is spent, asked by a frame that draws nothing. The budget runs
    // from the first drawn frame.
    internal bool Expired => Spent(_timestamp());

    private bool Spent(long now) => _exitAfterTicks > 0 && _stages[^1] >= 0 && now - _stages[^1] >= _exitAfterTicks;

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
    // are a monotonic counter. The two are anchored once.
    private static long ProcessStartTimestamp()
    {
        using Process process = Process.GetCurrentProcess();
        double elapsedSeconds = (DateTime.Now - process.StartTime).TotalSeconds;

        return Stopwatch.GetTimestamp() - (long)(elapsedSeconds * Stopwatch.Frequency);
    }

    // Called on the first frame, before any row reaches the writer thread. One thread touches the
    // writer at a time.
    private void WriteBootTrace()
    {
        long processStart = ProcessStartTimestamp();

        _writer.WriteLine("# capsule boot trace: milliseconds since process start");
        for (int i = 0; i < StageNames.Length; i++)
        {
            // A headless run has no device and loads no media, and never crosses those stages.
            if (_stages[i] < 0)
            {
                continue;
            }

            NumberText stage = new(_row);
            stage.Add("# ");
            stage.Add(StageNames[i]);
            stage.Add(",");
            stage.Add(Milliseconds(_stages[i] - processStart), "F3");
            _writer.WriteLine(stage.Written);
        }

        _writer.WriteLine("intervalMs,updateMs,viewMs,drawMs,steps,gen0,bytes");
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
            line.Add(row.ViewMs, "F3");
            line.Add(",");
            line.Add(row.DrawMs, "F3");
            line.Add(",");
            line.Add(row.Steps);
            line.Add(",");
            line.Add(row.Gen0);
            line.Add(",");
            line.Add(row.Bytes);
            _writer.WriteLine(line.Written);
        }

        _writer.Flush();
    }

    private readonly record struct Row(double IntervalMs, double UpdateMs, double ViewMs, double DrawMs, int Steps, int Gen0, long Bytes);
}
