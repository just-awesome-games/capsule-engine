using System.Globalization;
using Capsule.Diagnostics;

namespace Capsule.Runtime;

// Capsule's standard command line in one place: what EngineBuilder.WithCommandLine parses and what
// --help prints. A malformed line throws a CommandLineException at the call site that passed it.
internal sealed class CommandLine
{
    // The development flags exist only where the development plane is on. A shipping build refuses
    // them like any option it does not declare, and a player cannot drive or probe it.
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
                    parsed.SceneName = Value(args, ref index, gameName, development, "--scene needs a scene class name.");
                    break;

                case "--frames" when development:
                    parsed.FramesPath = Value(args, ref index, gameName, development, "--frames needs a path.");
                    if (!TrySeconds(args, ref index, out double? seconds))
                    {
                        throw Refuse(gameName, development, "--frames takes a finite duration in seconds above zero.");
                    }

                    parsed.FramesSeconds = seconds;
                    break;

                case "--saves":
                    parsed.SavesPath = Value(args, ref index, gameName, development, "--saves needs a directory.");
                    break;

                default:
                    throw Refuse(gameName, development, $"unknown option '{flag}'.");
            }
        }

        return parsed;
    }

    internal static CommandLineException Refuse(string gameName, string message) =>
        Refuse(gameName, Development.IsSupported, message);

    internal static CommandLineException Help(string gameName) =>
        new("The command line asked for the usage block.", UsageFor(gameName), helpRequested: true);

    private static CommandLineException Refuse(string gameName, bool development, string message) =>
        new(message, UsageFor(gameName, development), helpRequested: false);

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

    // --frames takes an optional duration, and a following token belongs to it only when it parses as
    // a number. A number the builder would reject is a malformed command line, caught here.
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
