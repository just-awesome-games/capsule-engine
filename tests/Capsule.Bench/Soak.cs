using System.Diagnostics;
using System.Globalization;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench;

/// <summary>The profiling lane's driver steps for 20 real seconds after 180 warm-up steps and presses nothing.</summary>
/// <remarks>
/// A sampling profiler gathers thousands of steady-state samples in that time. The driver then
/// prints the steps it ran, their mean, what they allocated and the managed heap around them, and
/// ends the run. A headless run builds no frame, and <see cref="SoakView"/> also builds one between
/// steps, as a presenting host does.
/// <para>It reads the clock once every 256 steps. The runtime's sampler stops a thread at its next safe
/// point, and a clock read every step would be that point and collect the samples of a light step.</para>
/// </remarks>
public class Soak : IInputDriver
{
    private const int WarmUpSteps = 180;

    private const int StepsPerClockRead = 256;

    // Null when the driver builds no frame.
    private readonly FrameView? _view;
    private long _startedAt;
    private long _threadBytesAtStart;
    private long _processBytesAtStart;
    private int _gen0AtStart;
    private int _gen1AtStart;
    private int _gen2AtStart;
    private long _heapAtStart;
    private long _heapMax;

    // Real seconds after warm-up. `--soak-seconds <n>` on the bench's command line sets it.
    internal static double Seconds { get; set; } = 20d;

    public Soak()
        : this(buildsView: false)
    {
    }

    private protected Soak(bool buildsView) => _view = buildsView ? new FrameView() : null;

    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;

        if (_view is not null)
        {
            scene.DrawFrame(_view);
        }

        if (tick == WarmUpSteps)
        {
            _gen0AtStart = GC.CollectionCount(0);
            _gen1AtStart = GC.CollectionCount(1);
            _gen2AtStart = GC.CollectionCount(2);
            _heapAtStart = _heapMax = GC.GetTotalMemory(forceFullCollection: false);
            _processBytesAtStart = GC.GetTotalAllocatedBytes(precise: true);
            _threadBytesAtStart = GC.GetAllocatedBytesForCurrentThread();
            _startedAt = Stopwatch.GetTimestamp();
        }
        else if (tick > WarmUpSteps && (tick - WarmUpSteps) % StepsPerClockRead == 0)
        {
            _heapMax = Math.Max(_heapMax, GC.GetTotalMemory(forceFullCollection: false));
            if (Stopwatch.GetElapsedTime(_startedAt).TotalSeconds >= Seconds)
            {
                Report(tick - WarmUpSteps);
                return false;
            }
        }

        return true;
    }

    // The thread's bytes are the step's and the build's. The process's add every other thread over the
    // same window, and the heap is read without a collection at the window's start, its end and every
    // clock read between.
    private void Report(long steps)
    {
        double meanMs = Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds / steps;
        long threadBytes = GC.GetAllocatedBytesForCurrentThread() - _threadBytesAtStart;
        long processBytes = GC.GetTotalAllocatedBytes(precise: true) - _processBytesAtStart;
        long heap = GC.GetTotalMemory(forceFullCollection: false);
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"#bench soak steps={steps} meanMs={meanMs:F4} view={_view is not null} threadBytes={threadBytes} processBytes={processBytes} gen0={GC.CollectionCount(0) - _gen0AtStart} gen1={GC.CollectionCount(1) - _gen1AtStart} gen2={GC.CollectionCount(2) - _gen2AtStart} heapStart={_heapAtStart} heapEnd={heap} heapMax={Math.Max(_heapMax, heap)}"));
    }
}
