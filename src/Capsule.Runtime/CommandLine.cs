using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Runtime.DevTools.Recordings;

namespace Capsule.Runtime;

// Capsule's standard command line in one place: what EngineBuilder.WithCommandLine parses and what
// --help prints. A malformed line throws a CommandLineException at the call site that passed it.
internal sealed class CommandLine
{
    // The development flags exist only where the development plane is on. A shipping build refuses
    // them like any option it does not declare, and a player cannot drive or probe it.
    private const string DevelopmentFlags = """
        starting a run
          --scene <Name>             boot the scene of that class name, else of that document key
          --payload <Name>           boot the opening scene with a new payload of that class name
          --seed <number>            start the random source from that seed, which a windowed run logs
          --language <tag>           start in the shipped language closest to that BCP 47 tag
          --driver <Name>            drive the run from the input driver of that class name
          --headless                 run with no window, playing a driver or a replay

        replaying
          --replay <file>            replay that recording, windowed at its pace unless --headless
          --fast-forward             play a windowed replay silently to its last tick and hold there
          --recordings <dir>         keep the run's recordings under that directory

        inspecting a headless run
          --trace <csv> <ticks> <names>
                                     write the named entities' panels at those ticks
          --capture <dir> <ticks>    write the frames at those ticks as PNGs
          --force                    let --trace or --capture write past its ceiling of ticks

        measuring
          --frames <csv>             write each frame's timing and memory
          --exit-after <seconds>     end a --frames run that long after its first frame
          --uncapped                 never wait for a frame: no vertical sync or frame cap, and a
                                     --frames headless run's frames back to back

        """;

    private const string ShippingFlags = """
        every build
          --saves <dir>              keep save documents under that directory
          --gpu-driver <name>        draw through that GPU driver: direct3d12, vulkan or metal
          --help                     print this and exit
        """;

    private const string TraceUsage = "--trace needs a CSV path, ticks and entity names: --trace trace.csv 100..160 Avatar.";

    private const string CaptureUsage = "--capture needs a directory and ticks: --capture frames 100..160.";

    internal bool HelpRequested { get; private set; }

    internal string? DriverName { get; private set; }

    internal string? SceneName { get; private set; }

    internal string? PayloadName { get; private set; }

    internal bool Headless { get; private set; }

    // The frame-timing CSV, null unless --frames was given.
    internal string? FramesPath { get; private set; }

    // The real seconds a --frames run lasts, null unless --exit-after was given.
    internal double? ExitAfterSeconds { get; private set; }

    internal bool Uncapped { get; private set; }

    // Whether a windowed replay plays silently to its hold instead of at its pace.
    internal bool FastForward { get; private set; }

    // The run's random seed, null unless --seed was given.
    internal ulong? Seed { get; private set; }

    // The requested language tag, null unless --language was given.
    internal string? Language { get; private set; }

    // The saves directory, null unless --saves was given.
    internal string? SavesPath { get; private set; }

    // The GPU driver's name, null unless --gpu-driver was given.
    internal string? GpuDriver { get; private set; }

    // The recording to replay, null unless --replay was given.
    internal string? ReplayPath { get; private set; }

    // The recordings directory, null unless --recordings was given.
    internal string? RecordingsPath { get; private set; }

    // The trace's CSV, ticks and entity names, all null unless --trace was given.
    internal string? TracePath { get; private set; }

    internal TickRange[]? TraceTicks { get; private set; }

    internal string[]? TraceNames { get; private set; }

    // The capture's directory and ticks, both null unless --capture was given.
    internal string? CapturePath { get; private set; }

    internal TickRange[]? CaptureTicks { get; private set; }

    // Whether --force lifts the trace's and the capture's ceilings.
    internal bool Force { get; private set; }

    private CommandLine()
    {
    }

    // `development` is the process's Development.IsSupported. It is a parameter to let a test read the
    // usage a shipping build prints.
    internal static string UsageFor(string gameName, bool development) =>
        $"usage: {gameName} [options]{Environment.NewLine}{(development ? DevelopmentFlags : "")}{ShippingFlags}";

    internal static CommandLine Parse(string[] args, string gameName) =>
        Parse(args, gameName, Development.IsSupported);

    // Throws a CommandLineException naming the first defect, which carries this game's usage block.
    internal static CommandLine Parse(string[] args, string gameName, bool development)
    {
        CommandLine parsed = new();
        HashSet<string> given = new(StringComparer.Ordinal);

        for (int index = 0; index < args.Length; index++)
        {
            string flag = args[index];

            if (!given.Add(flag))
            {
                throw Refuse(gameName, development, $"{flag} was given more than once.");
            }

            switch (flag)
            {
                case "--help":
                    parsed.HelpRequested = true;
                    break;

                case "--driver" when development:
                    parsed.DriverName = Value(args, ref index, gameName, development, "--driver needs a driver name.");
                    break;

                case "--headless" when development:
                    parsed.Headless = true;
                    break;

                case "--scene" when development:
                    parsed.SceneName = Value(args, ref index, gameName, development, "--scene needs a scene class name or a scene document key.");
                    break;

                case "--payload" when development:
                    parsed.PayloadName = Value(args, ref index, gameName, development, "--payload needs a payload class name.");
                    break;

                case "--frames" when development:
                    parsed.FramesPath = Value(args, ref index, gameName, development, "--frames needs a path.");
                    break;

                case "--exit-after" when development:
                    string after = Value(args, ref index, gameName, development, "--exit-after needs a number of seconds.");
                    parsed.ExitAfterSeconds = double.TryParse(after, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds) && seconds > 0d
                        ? seconds
                        : throw Refuse(gameName, development, $"--exit-after takes a finite number of seconds above zero, not '{after}'.");
                    break;

                case "--seed" when development:
                    string seed = Value(args, ref index, gameName, development, "--seed needs a seed.");
                    parsed.Seed = ulong.TryParse(seed, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsedSeed)
                        ? parsedSeed
                        : throw Refuse(gameName, development, $"--seed takes a whole number from 0 to {ulong.MaxValue.ToString(CultureInfo.InvariantCulture)}, not '{seed}'.");
                    break;

                case "--language" when development:
                    parsed.Language = Value(args, ref index, gameName, development, "--language needs a BCP 47 language tag.");
                    break;

                case "--uncapped" when development:
                    parsed.Uncapped = true;
                    break;

                case "--fast-forward" when development:
                    parsed.FastForward = true;
                    break;

                case "--replay" when development:
                    parsed.ReplayPath = Value(args, ref index, gameName, development, "--replay needs a recording file.");
                    break;

                case "--recordings" when development:
                    parsed.RecordingsPath = Value(args, ref index, gameName, development, "--recordings needs a directory.");
                    break;

                case "--trace" when development:
                    parsed.TracePath = Value(args, ref index, gameName, development, TraceUsage);
                    string ticks = Value(args, ref index, gameName, development, TraceUsage);
                    parsed.TraceTicks = Ticks("--trace", ticks, gameName, development);
                    string names = Value(args, ref index, gameName, development, TraceUsage);
                    parsed.TraceNames = names.Split(',');
                    if (Array.Exists(parsed.TraceNames, string.IsNullOrWhiteSpace))
                    {
                        throw Refuse(gameName, development, $"--trace takes a comma list of entity names, Scene, Camera or *, not '{names}'.");
                    }

                    break;

                case "--capture" when development:
                    parsed.CapturePath = Value(args, ref index, gameName, development, CaptureUsage);
                    parsed.CaptureTicks = Ticks("--capture", Value(args, ref index, gameName, development, CaptureUsage), gameName, development);
                    break;

                case "--force" when development:
                    parsed.Force = true;
                    break;

                case "--saves":
                    parsed.SavesPath = Value(args, ref index, gameName, development, "--saves needs a directory.");
                    break;

                case "--gpu-driver":
                    parsed.GpuDriver = Value(args, ref index, gameName, development, "--gpu-driver needs a driver name: direct3d12, vulkan or metal.");
                    break;

                default:
                    throw Refuse(gameName, development, $"unknown option '{flag}'.");
            }
        }

        // A recording carries the run's input, opening, seed and language.
        if (parsed.ReplayPath is not null)
        {
            foreach (string conflict in (ReadOnlySpan<string>)["--driver", "--scene", "--payload", "--seed", "--language"])
            {
                if (given.Contains(conflict))
                {
                    throw Refuse(gameName, development, $"--replay and {conflict} were both given, and the recording supplies what {conflict} sets. Drop {conflict}.");
                }
            }
        }

        // The run's timer is the frame diagnostics', which count from the first drawn frame.
        if (parsed.ExitAfterSeconds is not null && parsed.FramesPath is null)
        {
            throw Refuse(gameName, development, "--exit-after ends a --frames run. Give --frames <csv> with it.");
        }

        // A fast-forward skips a windowed replay's frames. A headless replay has none to skip, and frame
        // diagnostics would time none.
        if (parsed.FastForward && (parsed.ReplayPath is null || parsed.Headless || parsed.FramesPath is not null))
        {
            throw Refuse(gameName, development, "--fast-forward plays a windowed replay to its last tick. Give it with --replay <file>, and without --headless or --frames.");
        }

        // A trace is written by a run with no window, which a recording or a driver plays as fast as it can.
        if (parsed.TracePath is not null && !parsed.Headless)
        {
            throw Refuse(gameName, development, "--trace writes a headless run. Give --headless with it, and --replay <file> or --driver <Name>.");
        }

        if (parsed.CapturePath is not null && !parsed.Headless)
        {
            throw Refuse(gameName, development, "--capture writes a headless run. Give --headless with it, and --replay <file> or --driver <Name>.");
        }

        if (parsed.Force && parsed.TracePath is null && parsed.CapturePath is null)
        {
            throw Refuse(gameName, development, "--force lifts the ceiling of --trace or --capture, and neither was given. Drop --force.");
        }

        return parsed;
    }

    internal static CommandLineException Refuse(string gameName, string message) =>
        Refuse(gameName, Development.IsSupported, message);

    internal static CommandLineException Help(string gameName) =>
        new("The command line asked for the usage block.", UsageFor(gameName, Development.IsSupported), helpRequested: true);

    private static CommandLineException Refuse(string gameName, bool development, string message) =>
        new(message, UsageFor(gameName, development), helpRequested: false);

    private static TickRange[] Ticks(string flag, string ticks, string gameName, bool development) =>
        TickRange.TryParseList(ticks)
            ?? throw Refuse(gameName, development, $"{flag} takes its ticks as a tick, a range such as 100..160 or 100.., or a comma list of them, not '{ticks}'.");

    // A value never starts with the flag prefix, and a missing value is caught here instead of
    // swallowing the flag that follows it. A blank token counts as missing.
    private static string Value(string[] args, ref int index, string gameName, bool development, string defect)
    {
        if (index + 1 >= args.Length
            || args[index + 1].StartsWith("--", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw Refuse(gameName, development, defect);
        }

        return args[++index];
    }
}
