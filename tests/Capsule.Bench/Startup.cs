using System.Diagnostics;
using System.Globalization;
using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Bench;

/// <summary>The load driver: prints the time from process start to the first step's request, then each of the first eight steps, and ends the run.</summary>
public sealed class Startup : IInputDriver
{
    private const int Steps = 8;

    private readonly double[] _stepMs = new double[Steps];
    private double _bootMs;
    private long _previous;
    private long _bytes;

    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        long now = Stopwatch.GetTimestamp();
        if (tick == 0)
        {
            using Process process = Process.GetCurrentProcess();
            _bootMs = (DateTime.Now - process.StartTime).TotalMilliseconds;
            _bytes = GC.GetTotalAllocatedBytes(precise: true);
        }
        else
        {
            _stepMs[tick - 1] = (now - _previous) * 1000.0 / Stopwatch.Frequency;
        }

        if (tick == Steps)
        {
            string steps = string.Join(',', _stepMs.Select(static ms => ms.ToString("F3", CultureInfo.InvariantCulture)));
            long heap = GC.GetTotalMemory(forceFullCollection: true);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"#bench startup bootMs={_bootMs:F1} bootBytes={_bytes} heap={heap} stepMs={steps}"));
            return false;
        }

        _previous = Stopwatch.GetTimestamp();
        return true;
    }
}
