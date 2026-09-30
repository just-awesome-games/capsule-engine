using System.Diagnostics;
using System.Globalization;
using System.Text;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench;

/// <summary>
/// The headless lane's driver: presses nothing and times every fixed step from one snapshot request
/// to the next, which under <c>--headless</c> is the step and nothing else. Between steps it builds
/// the frame a presenting host would and times that apart. After 180 warm-up steps it measures 600,
/// prints them on one tagged line the suite parses, and ends the run.
/// </summary>
/// <remarks>
/// It also counts the bytes the steps and the builds allocate on this thread. The fixed step and
/// the build both run here, and a scene spawns no work on another thread once it has loaded.
/// </remarks>
public sealed class StepTimer : IInputDriver
{
    private const int WarmUpSteps = 180;

    private const int MeasuredSteps = 600;

    private const string Tag = "#bench steps gen0=";

    private readonly double[] _stepMs = new double[MeasuredSteps];
    private readonly double[] _viewMs = new double[MeasuredSteps];

    // The scene's own view builds only when read, and a headless run reads none. The driver builds
    // into its own view the same way, so the step's clock never sees the build.
    private readonly FrameView _view = new();
    private long _previous;
    private long _allocatedBeforeStep;
    private long _stepBytes;
    private long _viewBytes;
    private int _gen0AtStart;

    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        long now = Stopwatch.GetTimestamp();
        long stepped = GC.GetAllocatedBytesForCurrentThread();

        scene.DrawFrame(_view);
        long built = Stopwatch.GetTimestamp();
        long drawn = GC.GetAllocatedBytesForCurrentThread();

        if (tick == WarmUpSteps)
        {
            _gen0AtStart = GC.CollectionCount(0);
        }
        else if (tick > WarmUpSteps)
        {
            _stepMs[tick - WarmUpSteps - 1] = Milliseconds(now - _previous);
            _viewMs[tick - WarmUpSteps - 1] = Milliseconds(built - now);
            _stepBytes += stepped - _allocatedBeforeStep;
            _viewBytes += drawn - stepped;
        }

        if (tick == WarmUpSteps + MeasuredSteps)
        {
            StringBuilder line = new(Tag);
            line.Append(GC.CollectionCount(0) - _gen0AtStart).Append(' ');
            line.Append(_stepBytes).Append(' ').Append(_viewBytes).Append(' ');
            line.AppendJoin(',', _stepMs.Select(static ms => ms.ToString("F4", CultureInfo.InvariantCulture))).Append(' ');
            line.AppendJoin(',', _viewMs.Select(static ms => ms.ToString("F4", CultureInfo.InvariantCulture)));
            Console.WriteLine(line);
            return false;
        }

        _allocatedBeforeStep = GC.GetAllocatedBytesForCurrentThread();
        _previous = Stopwatch.GetTimestamp();
        return true;
    }

    // The gen-0 collections over the measured steps, the bytes the steps and the builds allocated,
    // each step's milliseconds and each build's that followed it; false for any other line.
    internal static bool TryParse(string line, out int gen0, out long stepBytes, out long viewBytes, out double[] stepMs, out double[] viewMs)
    {
        gen0 = 0;
        stepBytes = 0;
        viewBytes = 0;
        stepMs = [];
        viewMs = [];
        if (!line.StartsWith(Tag, StringComparison.Ordinal))
        {
            return false;
        }

        string[] fields = line[Tag.Length..].Split(' ');
        gen0 = int.Parse(fields[0], CultureInfo.InvariantCulture);
        stepBytes = long.Parse(fields[1], CultureInfo.InvariantCulture);
        viewBytes = long.Parse(fields[2], CultureInfo.InvariantCulture);
        stepMs = Parse(fields[3]);
        viewMs = Parse(fields[4]);

        return true;
    }

    private static double[] Parse(string samples) =>
        Array.ConvertAll(samples.Split(','), static sample => double.Parse(sample, CultureInfo.InvariantCulture));

    private static double Milliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
