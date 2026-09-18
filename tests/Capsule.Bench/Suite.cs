using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Capsule.Bench.Logic;
using Capsule.Bench.Logic.Drivers;

namespace Capsule.Bench;

// `suite [--label <text>]`: every workload in Workloads.All, each in a child process of this same
// binary on the engine's own command line, in the lane its [Workload] attribute names, then one
// record under results/ beside this source.
internal static class Suite
{
    private const string ArtifactsDirectory = "artifacts/bench";

    private const double SecondsPerWindowedWorkload = 6d;

    private const int WarmUpFrames = 60;

    internal static int Run(string[] args)
    {
        string? label = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--label" && index + 1 < args.Length && label is null)
            {
                label = args[++index];
                continue;
            }

            Console.Error.WriteLine($"unknown suite option '{args[index]}'.");
            Console.Error.WriteLine("usage: Capsule.Bench suite [--label <text>]");
            return 2;
        }

        label ??= "unlabelled";

        // Every lane is decided before anything runs, so a scene without one fails the suite at once.
        List<(string Name, WorkloadAttribute Lane)> lanes = [];
        foreach (Type scene in Workloads.All)
        {
            if (scene.GetCustomAttribute<WorkloadAttribute>() is not { } lane)
            {
                Console.Error.WriteLine($"bench: {scene.Name} is a registered scene with no [Workload(WorkloadKind.Simulation)] or [Workload(WorkloadKind.Rendering)] attribute.");
                return 1;
            }

            lanes.Add((scene.Name, lane));
        }

        string executable = Environment.ProcessPath!;
        DateTime started = DateTime.UtcNow;
        long startedAt = Stopwatch.GetTimestamp();
        List<WorkloadRecord> workloads = [];

        foreach ((string name, WorkloadAttribute lane) in lanes)
        {
            Console.WriteLine($"bench: {name} ({lane.Kind}, {Program.Describe(lane.Surface)})");

            WorkloadRecord? workload = lane.Kind == WorkloadKind.Simulation
                ? Headless(executable, name, lane)
                : Windowed(executable, name, lane, label);
            if (workload is null)
            {
                return 1;
            }

            workloads.Add(workload);
            Console.WriteLine(Summary(workload));
        }

        string sourceDirectory = SourceDirectory();
        SuiteRecord record = new(
            started.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            label,
            Machine.Commit(sourceDirectory),
            Machine.Configuration,
            Machine.Os,
            Machine.Cpu(),
            Machine.Gpu(),
            Machine.EngineVersion(),
            workloads);

        string path = Path.Combine(sourceDirectory, "results", started.ToString("yyyy-MM-dd'T'HH-mm-ss'Z'", CultureInfo.InvariantCulture) + ".json");
        record.Save(path);
        Console.WriteLine($"bench: wrote {path}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bench: {Stopwatch.GetElapsedTime(startedAt).TotalSeconds:F1} s for {workloads.Count} workloads"));

        return 0;
    }

    // `--scene X --headless --driver StepTimer`: the driver times every step and prints the samples.
    private static WorkloadRecord? Headless(string executable, string workload, WorkloadAttribute lane)
    {
        int gen0 = 0;
        double[]? stepMs = null;

        bool ran = RunChild(executable, workload, ["--headless", "--driver", nameof(StepTimer)], line =>
        {
            if (StepTimer.TryParse(line, out gen0, out double[] parsed))
            {
                stepMs = parsed;
                return true;
            }

            return false;
        });

        if (!ran || stepMs is null)
        {
            Console.Error.WriteLine($"bench: {workload} reported no timed steps.");
            return null;
        }

        Percentiles step = Percentiles.Of(stepMs);

        return new WorkloadRecord(workload, "headless", Program.Describe(lane.Surface), stepMs.Length, null, null, new StepTiming(step.Median, step.P95), null, gen0, null);
    }

    // `--scene X --frames <csv> 6`: the host writes a row per frame; the first WarmUpFrames are
    // dropped, and gen-0 is counted from the last warm-up row so the first measured frame counts.
    private static WorkloadRecord? Windowed(string executable, string workload, WorkloadAttribute lane, string label)
    {
        string csv = Path.Combine(ArtifactsDirectory, workload + ".csv");
        bool captured = workload == "Still";

        List<string> arguments = ["--frames", csv, SecondsPerWindowedWorkload.ToString(CultureInfo.InvariantCulture)];
        if (captured)
        {
            arguments.AddRange(["--driver", nameof(StillCapture)]);
        }

        if (!RunChild(executable, workload, arguments, static _ => false))
        {
            return null;
        }

        List<FrameRow> frames = FrameRow.Read(csv);
        if (frames.Count <= WarmUpFrames)
        {
            Console.Error.WriteLine($"bench: {workload} drew {frames.Count} frames, none past warm-up.");
            return null;
        }

        int gen0 = frames[^1].Gen0 - frames[WarmUpFrames - 1].Gen0;
        frames.RemoveRange(0, WarmUpFrames);

        string? sha256 = null;
        if (captured)
        {
            string kept = Path.Combine(ArtifactsDirectory, $"still-{label}.png");
            File.Move(StillCapture.Path, kept, overwrite: true);
            using FileStream stream = File.OpenRead(kept);
            sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        Percentiles draw = Percentiles.Of(frames.ConvertAll(static frame => frame.DrawMs).ToArray());
        Percentiles interval = Percentiles.Of(frames.ConvertAll(static frame => frame.IntervalMs).ToArray());

        return new WorkloadRecord(
            workload,
            "windowed",
            Program.Describe(lane.Surface),
            null,
            frames.Count,
            new DrawTiming(draw.Median, draw.P95, draw.Max),
            null,
            new IntervalTiming(interval.Median, interval.P95, interval.Max),
            gen0,
            sha256);
    }

    // Runs the child to its end, offering each line it prints to report and relaying the ones it declines.
    private static bool RunChild(string executable, string workload, IReadOnlyList<string> arguments, Func<string, bool> report)
    {
        ProcessStartInfo start = new(executable) { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("--scene");
        start.ArgumentList.Add(workload);
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process child = Process.Start(start)!;
        while (child.StandardOutput.ReadLine() is { } line)
        {
            if (!report(line))
            {
                Console.WriteLine("  " + line);
            }
        }

        child.WaitForExit();
        if (child.ExitCode != 0)
        {
            Console.Error.WriteLine($"bench: {workload} exited with {child.ExitCode}.");
            return false;
        }

        return true;
    }

    private static string Summary(WorkloadRecord workload) =>
        workload.DrawMs is { } draw && workload.IntervalMs is { } interval
            ? string.Create(CultureInfo.InvariantCulture, $"bench: {workload.Name} frames={workload.Frames} drawMs median={draw.Median:F3} p95={draw.P95:F3} max={draw.Max:F3} intervalMs max={interval.Max:F3} gen0={workload.Gen0Collections}")
            : string.Create(CultureInfo.InvariantCulture, $"bench: {workload.Name} steps={workload.Steps} stepMs median={workload.StepMs!.Median:F3} p95={workload.StepMs.P95:F3} gen0={workload.Gen0Collections}");

    // Beside this source file wherever the suite is run from, so the record lands where it is committed.
    private static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;

    // One row of the CSV FrameDiagnostics writes, after its commented boot trace and header.
    private readonly record struct FrameRow(double IntervalMs, double DrawMs, int Gen0)
    {
        internal static List<FrameRow> Read(string path)
        {
            List<FrameRow> rows = [];
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0 || line[0] == '#' || line[0] == 'i')
                {
                    continue;
                }

                string[] fields = line.Split(',');
                rows.Add(new FrameRow(
                    double.Parse(fields[0], CultureInfo.InvariantCulture),
                    double.Parse(fields[2], CultureInfo.InvariantCulture),
                    int.Parse(fields[4], CultureInfo.InvariantCulture)));
            }

            return rows;
        }
    }

    // Nearest-rank percentiles over the sorted values, rounded to microseconds; an even count's
    // median is the mean of its two middle values.
    private readonly record struct Percentiles(double Median, double P95, double Max)
    {
        internal static Percentiles Of(double[] values)
        {
            Array.Sort(values);

            int count = values.Length;
            double median = count % 2 == 1 ? values[count / 2] : (values[(count / 2) - 1] + values[count / 2]) / 2d;
            double p95 = values[Math.Max(0, (int)Math.Ceiling(0.95 * count) - 1)];

            return new Percentiles(Math.Round(median, 3), Math.Round(p95, 3), Math.Round(values[^1], 3));
        }
    }
}
