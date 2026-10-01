using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Capsule.Bench;

// `assets [--label <text>] [--runs N]`: the asset corpus's logic project built once per case run, each
// build timed whole and its asset tool timed by MSBuild's performance summary, then one record under
// results/assets/ beside this source.
internal static partial class AssetBench
{
    private const string ToolTarget = "CapsuleRunBuildTool";

    private const int TouchedFiles = 100;

    internal static int Run(string[] args)
    {
        string? label = null;
        int runs = 3;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--label" && index + 1 < args.Length && label is null)
            {
                label = args[++index];
                continue;
            }

            if (args[index] == "--runs" && index + 1 < args.Length && int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out runs) && runs > 0)
            {
                index++;
                continue;
            }

            Console.Error.WriteLine($"unknown assets option '{args[index]}'.");
            Console.Error.WriteLine("usage: Capsule.Bench assets [--label <text>] [--runs N]");
            return 2;
        }

        label ??= "unlabelled";

        DateTime started = DateTime.UtcNow;
        long startedAt = Stopwatch.GetTimestamp();
        AssetCorpus corpus = AssetCorpus.Ensure(Path.GetFullPath(Path.Combine(Records.SourceDirectory(), "..", "..")));
        string[] files = Directory.GetFiles(corpus.Assets, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        long bytes = files.Sum(static file => new FileInfo(file).Length);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bench: corpus {files.Length} files, {bytes / (1024d * 1024d):F0} MiB"));

        // Each case's change for its run's edit number, 1 upward. Edit 0 writes the canonical file.
        (string Name, Action<int> Change)[] cases =
        [
            ("no-change", static _ => { }),
            ("texture-edit", edit => corpus.WriteTexture(17, edit)),
            ("atlas-member-edit", edit => corpus.WriteSprite(3, 42, edit)),
            ("r8-edit", edit => corpus.WriteMask(5, edit)),
            ("scene-edit", edit => corpus.WriteScene(9, edit)),
            ("import-edit", edit => corpus.WriteRoom(11, edit)),
            ("tileset-edit", edit => corpus.WritePalette(2, edit)),
            ("shader-edit", edit => corpus.WriteShader(1, edit)),
            ("touch-only", _ => Touch(files)),
        ];

        // A run that was stopped part way leaves its last edit behind.
        foreach ((string _, Action<int> change) in cases)
        {
            change(0);
        }

        if (Dotnet(["restore", corpus.LogicProject]) is null)
        {
            return 1;
        }

        // Builds the build project and the engine in the corpus's configuration, which no case times.
        Console.WriteLine("bench: warm-up build");
        if (Build(corpus) is null)
        {
            return 1;
        }

        List<AssetCaseRecord> rows = [];

        foreach (string capsuleObj in Directory.GetDirectories(Path.Combine(Path.GetDirectoryName(corpus.LogicProject)!, "obj"), "capsule", SearchOption.AllDirectories))
        {
            Directory.Delete(capsuleObj, recursive: true);
        }

        if (Build(corpus) is not { } cold)
        {
            return 1;
        }

        rows.Add(new AssetCaseRecord("cold", cold.WallMs, cold.ToolMs, 1));
        Console.WriteLine(Summary(rows[^1]));

        foreach ((string name, Action<int> change) in cases)
        {
            List<BuildTiming> timings = [];
            for (int run = 1; run <= runs; run++)
            {
                change(run);
                if (Build(corpus) is not { } timing)
                {
                    return 1;
                }

                timings.Add(timing);
            }

            rows.Add(new AssetCaseRecord(name, Percentiles.Of([.. timings.Select(static t => t.WallMs)]).Median, Percentiles.Of([.. timings.Select(static t => t.ToolMs)]).Median, runs));
            Console.WriteLine(Summary(rows[^1]));
        }

        AssetsRecord record = new(
            Records.Timestamp(started),
            label,
            Machine.Commit(Records.SourceDirectory()),
            Machine.Configuration,
            Machine.Os,
            Machine.Cpu(),
            Machine.Gpu(),
            Machine.EngineVersion(),
            AssetCorpus.Version,
            files.Length,
            bytes,
            rows);

        string path = Records.Save(record, started, "assets");
        Console.WriteLine($"bench: wrote {path}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bench: {Stopwatch.GetElapsedTime(startedAt).TotalMinutes:F1} min for {rows.Count} cases"));

        return 0;
    }

    // Write times move forward on every hundredth-or-so file, and no byte changes, as a checkout leaves them.
    private static void Touch(string[] files)
    {
        DateTime now = DateTime.UtcNow;
        for (int index = 0; index < files.Length; index += files.Length / TouchedFiles)
        {
            File.SetLastWriteTimeUtc(files[index], now);
        }
    }

    // One incremental build of the logic project: its wall time, and the asset tool's target from the
    // performance summary. The target runs on every build.
    private static BuildTiming? Build(AssetCorpus corpus)
    {
        long startedAt = Stopwatch.GetTimestamp();
        string? output = Dotnet(["build", corpus.LogicProject, "--no-restore", "-nologo", "-tl:off", "-clp:PerformanceSummary"]);
        double wallMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        if (output is null)
        {
            return null;
        }

        if (ToolTime().Match(output) is not { Success: true } tool)
        {
            Console.Error.WriteLine($"bench: the build's performance summary names no {ToolTarget} target.");
            return null;
        }

        return new BuildTiming(Math.Round(wallMs), double.Parse(tool.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    // The command's standard output, or null after relaying its tail when it failed.
    private static string? Dotnet(IReadOnlyList<string> arguments)
    {
        ProcessStartInfo start = new("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process dotnet = Process.Start(start)!;
        Task<string> error = dotnet.StandardError.ReadToEndAsync();
        string output = dotnet.StandardOutput.ReadToEnd();
        dotnet.WaitForExit();
        if (dotnet.ExitCode == 0)
        {
            return output;
        }

        foreach (string line in output.Split('\n').TakeLast(40))
        {
            Console.Error.WriteLine("  " + line.TrimEnd());
        }

        Console.Error.Write(error.Result);
        Console.Error.WriteLine($"bench: dotnet {arguments[0]} exited with {dotnet.ExitCode}.");

        return null;
    }

    private static string Summary(AssetCaseRecord row) =>
        string.Create(CultureInfo.InvariantCulture, $"bench: {row.Case} wallMs={row.WallMs:F0} toolMs={row.ToolMs:F0} runs={row.Runs}");

    // A target summary row: "  27712 ms  CapsuleRunBuildTool  1 calls".
    [GeneratedRegex(@"^\s*(\d+) ms\s+" + ToolTarget + @"\s+\d+ calls", RegexOptions.Multiline)]
    private static partial Regex ToolTime();

    private readonly record struct BuildTiming(double WallMs, double ToolMs);
}
