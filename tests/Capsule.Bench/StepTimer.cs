using System.Diagnostics;
using System.Globalization;
using System.Text;
using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Bench;

/// <summary>
/// The headless lane's driver: presses nothing and times every fixed step from one snapshot request
/// to the next, which under <c>--headless</c> is the step and nothing else. After 180 warm-up steps
/// it measures 600, prints them on one tagged line the suite parses, and ends the run.
/// </summary>
public sealed class StepTimer : IInputDriver
{
    private const int WarmUpSteps = 180;

    private const int MeasuredSteps = 600;

    private const string Tag = "#bench steps gen0=";

    private readonly double[] _stepMs = new double[MeasuredSteps];
    private long _previous;
    private int _gen0AtStart;

    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        long now = Stopwatch.GetTimestamp();

        if (tick == WarmUpSteps)
        {
            _gen0AtStart = GC.CollectionCount(0);
        }
        else if (tick > WarmUpSteps)
        {
            _stepMs[tick - WarmUpSteps - 1] = (now - _previous) * 1000.0 / Stopwatch.Frequency;
        }

        if (tick == WarmUpSteps + MeasuredSteps)
        {
            StringBuilder line = new(Tag);
            line.Append(GC.CollectionCount(0) - _gen0AtStart).Append(' ');
            line.AppendJoin(',', _stepMs.Select(static ms => ms.ToString("F4", CultureInfo.InvariantCulture)));
            Console.WriteLine(line);
            return false;
        }

        _previous = now;
        return true;
    }

    // The gen-0 collections over the measured steps and each step's milliseconds; false for any other line.
    internal static bool TryParse(string line, out int gen0, out double[] stepMs)
    {
        gen0 = 0;
        stepMs = [];
        if (!line.StartsWith(Tag, StringComparison.Ordinal))
        {
            return false;
        }

        string[] fields = line[Tag.Length..].Split(' ');
        gen0 = int.Parse(fields[0], CultureInfo.InvariantCulture);
        stepMs = Array.ConvertAll(fields[1].Split(','), static sample => double.Parse(sample, CultureInfo.InvariantCulture));

        return true;
    }
}
