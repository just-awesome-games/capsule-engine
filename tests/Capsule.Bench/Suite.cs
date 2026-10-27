using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Capsule.Bench.Logic;
using Capsule.Bench.Logic.Drivers;
using Capsule.Bench.Logic.Scenes;

namespace Capsule.Bench;

// `suite [--label <text>] [--lane simulation|rendering|paced] [--uncapped]`: every workload in
// Workloads.All, or those of one lane, each in a child process of this same binary on the engine's own
// command line, once in each lane its [Workload] attributes name, then the sound mix in this process
// with the headless lane, then one record under results/ beside this source.
internal static class Suite
{
    private const string ArtifactsDirectory = "artifacts/bench";

    private const double SecondsPerFramedWorkload = 6d;

    // The window settles one late present within its first 80 frames on every run. 120 keeps it
    // out of the measured window at 165 Hz and a fortiori at 60.
    private const int WarmUpFrames = 120;

    // The paced lane drops its first second instead, which carries the run's first compilations. An
    // overloaded workload runs a frame a second or slower, and a frame count would drop all of them.
    private const double PacedWarmUpMs = 1000d;

    private const string Usage = "usage: Capsule.Bench suite [--label <text>] [--lane simulation|rendering|paced] [--uncapped]";

    internal static int Run(string[] args)
    {
        string? label = null;
        WorkloadKind? only = null;
        bool uncapped = false;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--label" && index + 1 < args.Length && label is null)
            {
                label = args[++index];
                continue;
            }

            if (args[index] == "--lane" && index + 1 < args.Length && only is null && TryLane(args[index + 1], out WorkloadKind lane))
            {
                only = lane;
                index++;
                continue;
            }

            if (args[index] == "--uncapped" && !uncapped)
            {
                uncapped = true;
                continue;
            }

            Console.Error.WriteLine($"unknown suite option '{args[index]}'.");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        // Only the windowed lane presents. An uncapped headless record would claim a flag that did nothing.
        if (uncapped && only is WorkloadKind.Simulation or WorkloadKind.Paced)
        {
            Console.Error.WriteLine($"--uncapped paces the rendering lane and --lane {LaneName(only.Value)} runs none of it. Drop one of them.");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        label ??= "unlabelled";

        // Every lane is decided before anything runs. A scene without one fails the suite at once.
        List<(string Name, WorkloadAttribute Lane)> lanes = [];
        foreach (Type scene in Workloads.All)
        {
            WorkloadAttribute[] declared = [.. scene.GetCustomAttributes<WorkloadAttribute>()];
            if (declared.Length == 0)
            {
                Console.Error.WriteLine($"bench: {scene.Name} is a registered scene with no [Workload(...)] attribute naming its lane.");
                return 1;
            }

            foreach (WorkloadAttribute lane in declared)
            {
                if (only is null || lane.Kind == only)
                {
                    lanes.Add((lane.Row ?? scene.Name, lane));
                }
            }
        }

        string executable = Environment.ProcessPath!;
        DateTime started = DateTime.UtcNow;
        long startedAt = Stopwatch.GetTimestamp();
        List<WorkloadRecord> workloads = [];

        foreach ((string name, WorkloadAttribute lane) in lanes)
        {
            Console.WriteLine($"bench: {name} ({lane.Kind}, {Program.Describe(lane.Surface)})");

            WorkloadRecord? workload = lane.Kind switch
            {
                WorkloadKind.Simulation => Headless(executable, name, lane),
                WorkloadKind.Paced => Paced(executable, name, lane),
                _ => Windowed(executable, name, lane, label, uncapped),
            };
            if (workload is null)
            {
                return 1;
            }

            workloads.Add(workload);
            Console.WriteLine(Summary(workload));
        }

        // The mix is timed in this process. It needs no window and runs with the headless lane.
        if (only is null or WorkloadKind.Simulation)
        {
            Console.WriteLine($"bench: {MixBench.Name} (mixer)");
            WorkloadRecord mix = MixBench.Run();
            workloads.Add(mix);
            Console.WriteLine(Summary(mix));
        }

        SuiteRecord record = new(
            Records.Timestamp(started),
            label,
            only is { } kind ? LaneName(kind) : "all",
            uncapped,
            Machine.Commit(Records.SourceDirectory()),
            Machine.Configuration,
            Machine.NativeAot,
            Machine.Os,
            Machine.Cpu(),
            Machine.Gpu(),
            Machine.EngineVersion(),
            workloads);

        string path = Records.Save(record, started);
        Console.WriteLine($"bench: wrote {path}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bench: {Stopwatch.GetElapsedTime(startedAt).TotalSeconds:F1} s for {workloads.Count} workloads"));

        return 0;
    }

    // `--scene X --headless --driver StepTimer`: the driver times every step and the frame built after
    // it, and prints the samples.
    private static WorkloadRecord? Headless(string executable, string workload, WorkloadAttribute lane)
    {
        int gen0 = 0;
        long stepBytes = 0;
        long viewBytes = 0;
        double[]? stepMs = null;
        double[]? viewMs = null;

        bool ran = RunChild(executable, workload, ["--headless", "--driver", nameof(StepTimer)], line =>
        {
            if (StepTimer.TryParse(line, out gen0, out stepBytes, out viewBytes, out double[] steps, out double[] views))
            {
                stepMs = steps;
                viewMs = views;
                return true;
            }

            return false;
        });

        if (!ran || stepMs is null || viewMs is null)
        {
            Console.Error.WriteLine($"bench: {workload} reported no timed steps.");
            return null;
        }

        Percentiles step = Percentiles.Of(stepMs);
        Percentiles view = Percentiles.Of(viewMs);

        return new WorkloadRecord(workload, "headless", Program.Describe(lane.Surface), stepMs.Length, null, null, null, new StepTiming(step.Median, step.P95), new StepTiming(view.Median, view.P95), stepBytes, viewBytes, null, null, gen0, null);
    }

    // `--scene X --frames <csv> 6`: the host writes a row per frame. The first WarmUpFrames are
    // dropped, and gen-0 is counted from the last warm-up row so the first measured frame counts.
    // Uncapped, the host presents without vertical sync and the interval is the true frame cost.
    private static WorkloadRecord? Windowed(string executable, string workload, WorkloadAttribute lane, string label, bool uncapped)
    {
        string csv = Path.Combine(ArtifactsDirectory, workload + ".csv");
        bool captured = workload is nameof(Still) or Still.Lit or Still.Shaded or Still.Hd;

        List<string> arguments = ["--frames", csv, SecondsPerFramedWorkload.ToString(CultureInfo.InvariantCulture)];
        if (captured)
        {
            arguments.AddRange(["--driver", nameof(StillCapture)]);
        }

        if (uncapped)
        {
            arguments.Add("--uncapped");
        }

        if (!RunChild(executable, workload, arguments, static _ => false) || MeasuredFrames(csv, workload, static _ => WarmUpFrames, out int gen0) is not { } frames)
        {
            return null;
        }

        string? sha256 = null;
        if (captured)
        {
            string kept = Path.Combine(ArtifactsDirectory, $"{workload}-{label}.png");
            File.Move(StillCapture.Path, kept, overwrite: true);
            using FileStream stream = File.OpenRead(kept);
            sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        return new WorkloadRecord(
            workload,
            "windowed",
            Program.Describe(lane.Surface),
            null,
            frames.Count,
            Percentiles.Of(frames.ConvertAll(static frame => frame.DrawMs).ToArray()),
            Percentiles.Of(frames.ConvertAll(static frame => frame.UpdateMs).ToArray()),
            null,
            BuiltViews(frames),
            null,
            null,
            frames.Sum(static frame => frame.Bytes),
            Percentiles.Of(frames.ConvertAll(static frame => frame.IntervalMs).ToArray()),
            gen0,
            sha256);
    }

    // `--scene X --headless --driver Idle --frames <csv> 6`: the host's frame loop with no window, paced to
    // the step rate. Each frame runs the steps its wall time bought, up to the step bound. A step that
    // overruns its length shows in the frames a player would see.
    private static WorkloadRecord? Paced(string executable, string workload, WorkloadAttribute lane)
    {
        string csv = Path.Combine(ArtifactsDirectory, workload + ".paced.csv");
        List<string> arguments = ["--headless", "--driver", nameof(Idle), "--frames", csv, SecondsPerFramedWorkload.ToString(CultureInfo.InvariantCulture)];
        if (!RunChild(executable, workload, arguments, static _ => false) || MeasuredFrames(csv, workload, PacedWarmUp, out int gen0) is not { } frames)
        {
            return null;
        }

        return new WorkloadRecord(
            workload,
            "paced",
            Program.Describe(lane.Surface),
            frames.Sum(static frame => frame.Steps),
            frames.Count,
            null,
            Percentiles.Of(frames.ConvertAll(static frame => frame.UpdateMs).ToArray()),
            null,
            BuiltViews(frames),
            null,
            null,
            frames.Sum(static frame => frame.Bytes),
            Percentiles.Of(frames.ConvertAll(static frame => frame.IntervalMs).ToArray()),
            gen0,
            null);
    }

    // The build times of the frames that ran a step, the only frames that build a view. Null when none did.
    private static StepTiming? BuiltViews(List<FrameRow> frames)
    {
        double[] built = [.. frames.Where(static frame => frame.Steps > 0).Select(static frame => frame.ViewMs)];
        if (built.Length == 0)
        {
            return null;
        }

        Percentiles view = Percentiles.Of(built);

        return new StepTiming(view.Median, view.P95);
    }

    // The CSV's frames past the warm-up frames warmUp counts, and the gen-0 collections over them,
    // counted from the last warm-up row so the first measured frame counts. Null when none is past
    // warm-up.
    private static List<FrameRow>? MeasuredFrames(string csv, string workload, Func<List<FrameRow>, int> warmUp, out int gen0)
    {
        gen0 = 0;
        List<FrameRow> frames = FrameRow.Read(csv);
        int dropped = Math.Max(1, warmUp(frames));
        if (frames.Count <= dropped)
        {
            Console.Error.WriteLine($"bench: {workload} ran {frames.Count} frames, none past warm-up.");
            return null;
        }

        gen0 = frames[^1].Gen0 - frames[dropped - 1].Gen0;
        frames.RemoveRange(0, dropped);

        return frames;
    }

    // The frames that began within the run's first PacedWarmUpMs.
    private static int PacedWarmUp(List<FrameRow> frames)
    {
        int count = 0;
        for (double elapsed = 0d; count < frames.Count && elapsed < PacedWarmUpMs; count++)
        {
            elapsed += frames[count].IntervalMs;
        }

        return count;
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

    private static string LaneName(WorkloadKind kind) => kind switch
    {
        WorkloadKind.Simulation => "simulation",
        WorkloadKind.Paced => "paced",
        _ => "rendering",
    };

    private static bool TryLane(string name, out WorkloadKind kind)
    {
        kind = name switch
        {
            "rendering" => WorkloadKind.Rendering,
            "paced" => WorkloadKind.Paced,
            _ => WorkloadKind.Simulation,
        };
        return name is "simulation" or "rendering" or "paced";
    }

    private static string Summary(WorkloadRecord workload) => workload switch
    {
        { Mode: "windowed", DrawMs: { } draw, UpdateMs: { } update, IntervalMs: { } interval } => string.Create(CultureInfo.InvariantCulture, $"bench: {workload.Name} frames={workload.Frames} drawMs median={draw.Median:F3} p95={draw.P95:F3} max={draw.Max:F3} updateMs p95={update.P95:F3} viewMs p95={workload.ViewMs?.P95:F3} intervalMs max={interval.Max:F3} frameBytes={workload.FrameBytes} gen0={workload.Gen0Collections}"),
        { Mode: "mixer", StepMs: { } block } => string.Create(CultureInfo.InvariantCulture, $"bench: {workload.Name} blocks={workload.Steps} blockMs median={block.Median:F3} p95={block.P95:F3} bytes={workload.StepBytes} gen0={workload.Gen0Collections}"),
        { Mode: "paced", UpdateMs: { } update, IntervalMs: { } interval } => string.Create(CultureInfo.InvariantCulture, $"bench: {workload.Name} paced frames={workload.Frames} steps={workload.Steps} intervalMs median={interval.Median:F3} p95={interval.P95:F3} max={interval.Max:F3} updateMs max={update.Max:F3} frameBytes={workload.FrameBytes} gen0={workload.Gen0Collections}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"bench: {workload.Name} steps={workload.Steps} stepMs median={workload.StepMs!.Median:F3} p95={workload.StepMs.P95:F3} viewMs median={workload.ViewMs!.Median:F3} p95={workload.ViewMs.P95:F3} stepBytes={workload.StepBytes} viewBytes={workload.ViewBytes} gen0={workload.Gen0Collections}"),
    };

    // One row of the CSV FrameDiagnostics writes, after its commented boot trace and header.
    private readonly record struct FrameRow(double IntervalMs, double UpdateMs, double ViewMs, double DrawMs, int Steps, int Gen0, long Bytes)
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
                    double.Parse(fields[1], CultureInfo.InvariantCulture),
                    double.Parse(fields[2], CultureInfo.InvariantCulture),
                    double.Parse(fields[3], CultureInfo.InvariantCulture),
                    int.Parse(fields[4], CultureInfo.InvariantCulture),
                    int.Parse(fields[5], CultureInfo.InvariantCulture),
                    long.Parse(fields[6], CultureInfo.InvariantCulture)));
            }

            return rows;
        }
    }
}
