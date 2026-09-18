using System.Globalization;
using Capsule.Diagnostics;

namespace Capsule.Runtime;

// Capsule's standard command line, in one place: what EngineBuilder.WithCommandLine parses, what
// --help prints, and what a rejected command line is answered with. Nothing is thrown, no driver is
// built and no scene is looked up here — a defect is held as Error so the fluent chain completes,
// and the builder reports it when the run starts.
internal sealed class CommandLine
{
    // The exit code a command line the engine will not run answers with.
    internal const int BadArgumentExitCode = 2;

    // A builder that was never given a command line: nothing asked for and nothing rejected.
    internal static readonly CommandLine None = new();

    // The development flags exist only where the development plane is on; a shipping build
    // refuses them as it does any option it never declared, so a player cannot drive or probe it.
    private const string DevelopmentFlags = """
          --driver <Name>            drive the run from the input driver of that class name
          --headless                 run with no window, which needs a driver
          --scene <Name>             boot the registered scene of that class name
          --frames <csv> [seconds]   write host frame timing, exiting after seconds when given

        """;

    private const string ShippingFlags = """
          --saves <dir>              keep save documents under that directory
          --help                     print this and exit
        """;

    private CommandLine()
    {
    }

    // The first defect found, or null for a command line the engine will run.
    internal string? Error { get; private set; }

    internal bool HelpRequested { get; private set; }

    internal string? DriverName { get; private set; }

    internal string? SceneName { get; private set; }

    internal bool Headless { get; private set; }

    // The frame-timing CSV and its optional capture duration, both null unless --frames was given.
    internal string? FramesPath { get; private set; }

    internal double? FramesSeconds { get; private set; }

    // The saves directory, null unless --saves was given.
    internal string? SavesPath { get; private set; }

    internal static string UsageFor(string gameName) => UsageFor(gameName, Development.IsSupported);

    internal static string UsageFor(string gameName, bool development) =>
        $"usage: {gameName} [options]{Environment.NewLine}{(development ? DevelopmentFlags : "")}{ShippingFlags}";

    // Writes message and the usage block to standard error and answers the process's exit code.
    internal static int Reject(string gameName, string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine(UsageFor(gameName));

        return BadArgumentExitCode;
    }

    internal static CommandLine Parse(string[] args) => Parse(args, Development.IsSupported);

    // `development` is the process's Development.IsSupported, a parameter so a test can parse as a
    // shipping build would.
    internal static CommandLine Parse(string[] args, bool development)
    {
        CommandLine parsed = new();
        HashSet<string> given = new(StringComparer.Ordinal);

        for (int index = 0; index < args.Length; index++)
        {
            string flag = args[index];

            if (!given.Add(flag))
            {
                return parsed.Refuse($"{flag} was given more than once.");
            }

            switch (flag)
            {
                case "--help":
                    parsed.HelpRequested = true;
                    break;

                case "--driver" when development:
                    if (!TryValue(args, ref index, out string driver))
                    {
                        return parsed.Refuse("--driver needs a driver name.");
                    }

                    parsed.DriverName = driver;
                    break;

                case "--headless" when development:
                    parsed.Headless = true;
                    break;

                case "--scene" when development:
                    if (!TryValue(args, ref index, out string scene))
                    {
                        return parsed.Refuse("--scene needs a scene class name.");
                    }

                    parsed.SceneName = scene;
                    break;

                case "--frames" when development:
                    if (!TryValue(args, ref index, out string frames))
                    {
                        return parsed.Refuse("--frames needs a path.");
                    }

                    if (!TrySeconds(args, ref index, out double? seconds))
                    {
                        return parsed.Refuse("--frames takes a finite duration in seconds above zero.");
                    }

                    parsed.FramesPath = frames;
                    parsed.FramesSeconds = seconds;
                    break;

                case "--saves":
                    if (!TryValue(args, ref index, out string saves))
                    {
                        return parsed.Refuse("--saves needs a directory.");
                    }

                    parsed.SavesPath = saves;
                    break;

                default:
                    return parsed.Refuse($"unknown option '{flag}'.");
            }
        }

        return parsed;
    }

    private CommandLine Refuse(string error)
    {
        Error ??= error;

        return this;
    }

    // A value never starts with the flag prefix, so a missing one is caught here rather than
    // swallowing the flag that follows it. Blank is missing too: every value reaches a setter that
    // rejects a blank path.
    private static bool TryValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length
            || args[index + 1].StartsWith("--", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = string.Empty;

            return false;
        }

        value = args[++index];

        return true;
    }

    // --frames takes an optional duration, so a following token is only its own when it parses as a
    // number; one that does but is not a duration the builder accepts is a malformed command line,
    // not a run that fails late.
    private static bool TrySeconds(string[] args, ref int index, out double? seconds)
    {
        seconds = null;

        if (index + 1 >= args.Length
            || !double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return true;
        }

        index++;

        if (!double.IsFinite(parsed) || parsed <= 0d)
        {
            return false;
        }

        seconds = parsed;

        return true;
    }
}
