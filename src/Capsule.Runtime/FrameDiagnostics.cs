using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Capsule.Runtime;

// Host timing capture written as the CSV EngineBuilder.WithFrameDiagnostics documents. The gen-0
// count is cumulative, and a window of rows reads its own collections as a difference. A row's
// interval and allocations cover the same span, from its frame's start to the next frame's, which
// closes the row. The last row closes at disposal.
internal sealed class FrameDiagnostics : IDisposable
{
    // Rows fill one of two buffers. A full buffer is handed to the writer thread and the other takes
    // over. The frame path writes the file once, for the boot trace on the first frame, and never
    // waits on the disk unless the writer falls a full buffer behind. An ungraceful kill loses at most
    // the buffer being filled and the buffer being written.
    private const int FlushEvery = 300;

    private readonly StreamWriter _writer;

    // One row's text, reused so writing a frame's row allocates nothing.
    private readonly char[] _row = new char[192];
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

    // The drawn frame whose row waits for the next frame's start, and where its spans began.
    private bool _open;
    private long _frameStart;
    private long _frameAllocated;
    private long _frameProcessAllocated;
    private Row _frame;

    // The frame in progress, which becomes the open one once it draws, or at the next frame's start when it
    // draws nothing.
    private bool _updating;
    private long _updateStart;
    private long _updateAllocated;
    private long _updateProcessAllocated;
    private double _updateMs;
    private double _viewMs;
    private int _steps;
    private long _tick;

    // builderEntered is the timestamp taken when the builder was created, the trace's first stage.
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

    // A boot stage, in the order boot passes through them and the trace writes them. The first is the
    // builder's entry, passed in, and the last is the first submitted frame, taken here. BeginUpdate
    // takes FirstUpdate.
    internal enum Stage
    {
        BuilderEntered,
        HostConstructed,
        DeviceReady,
        SceneAssetsLoaded,
        FirstUpdate,
        FirstDraw,
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

    // Closes the previous frame's row at this frame's start.
    internal void BeginUpdate()
    {
        long now = _timestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long processAllocated = GC.GetTotalAllocatedBytes(precise: false);

        // The first update is the trace's last stage before the draw it feeds.
        if (_stages[(int)Stage.FirstUpdate] < 0)
        {
            _stages[(int)Stage.FirstUpdate] = now;
        }

        OpenUndrawn();
        Close(now, allocated, processAllocated);

        _updating = true;
        _updateStart = now;
        _updateAllocated = allocated;
        _updateProcessAllocated = processAllocated;
        _sectionStart = now;
    }

    // steps: The fixed steps the update ran, zero on a frame that had not accumulated one. tick: The
    // run's tick once they ran, the state the frame shows.
    internal void EndUpdate(int steps, long tick)
    {
        long now = _timestamp();
        _updateMs = Milliseconds(now - _sectionStart);
        _steps = steps;
        _tick = tick;
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

    // Ends the frame's sections, leaving its row open for the next frame's start, and returns whether the
    // run's time budget is spent.
    internal bool EndDraw()
    {
        long now = _timestamp();
        Open(_viewMs, Milliseconds(now - _sectionStart));

        if (_stages[^1] < 0)
        {
            _stages[^1] = now;

            // Written after the frame's own timestamps are taken. Resolving the process start costs
            // milliseconds, which then land on the first interval and not on the trace.
            WriteBootTrace();
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
        OpenUndrawn();
        Close(_timestamp(), GC.GetAllocatedBytesForCurrentThread(), GC.GetTotalAllocatedBytes(precise: false));
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
        for (int i = 0; i < _stages.Length; i++)
        {
            // A headless run has no device and loads no media, and never crosses those stages.
            if (_stages[i] < 0)
            {
                continue;
            }

            // The stage's name in camel case, such as builderEntered.
            string name = ((Stage)i).ToString();
            NumberText stage = new(_row);
            stage.Add("# ");
            stage.Add([char.ToLowerInvariant(name[0])]);
            stage.Add(name.AsSpan(1));
            stage.Add(",");
            stage.Add(Milliseconds(_stages[i] - processStart), "F3");
            _writer.WriteLine(stage.Written);
        }

        _writer.WriteLine("tick,intervalMs,updateMs,viewMs,drawMs,steps,gen0,bytes,processBytes,heapBytes");
        _writer.Flush();
    }

    // Opens the row of the frame in progress, its sections timed.
    private void Open(double viewMs, double drawMs)
    {
        _frame = new Row(
            _tick,
            0,
            _updateMs,
            viewMs,
            drawMs,
            _steps,
            GC.CollectionCount(0),
            0,
            0,
            GC.GetTotalMemory(forceFullCollection: false));
        _frameStart = _updateStart;
        _frameAllocated = _updateAllocated;
        _frameProcessAllocated = _updateProcessAllocated;
        _open = true;
        _updating = false;
    }

    // Opens the row of an update that drew nothing, such as a minimized or exiting frame, with no view or
    // draw. Rows start at the first drawn frame, whose boot trace and header precede them.
    private void OpenUndrawn()
    {
        if (!_updating)
        {
            return;
        }

        _updating = false;
        if (_stages[^1] >= 0)
        {
            Open(0, 0);
        }
    }

    // Writes the open row, its spans ending at now, and hands a full buffer to the writer.
    private void Close(long now, long allocated, long processAllocated)
    {
        if (!_open)
        {
            return;
        }

        _rows[_count++] = _frame with
        {
            IntervalMs = Milliseconds(now - _frameStart),
            Bytes = allocated - _frameAllocated,
            ProcessBytes = processAllocated - _frameProcessAllocated,
        };
        _open = false;

        if (_count == FlushEvery)
        {
            _full.Add(_rows);
            _rows = _free.Take();
            _count = 0;
        }
    }

    private void Write(Row[] rows, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Row row = rows[i];
            NumberText line = new(_row);
            line.Add(row.Tick);
            line.Add(",");
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
            line.Add(",");
            line.Add(row.ProcessBytes);
            line.Add(",");
            line.Add(row.HeapBytes);
            _writer.WriteLine(line.Written);
        }

        _writer.Flush();
    }

    private readonly record struct Row(
        long Tick,
        double IntervalMs,
        double UpdateMs,
        double ViewMs,
        double DrawMs,
        int Steps,
        int Gen0,
        long Bytes,
        long ProcessBytes,
        long HeapBytes);
}
