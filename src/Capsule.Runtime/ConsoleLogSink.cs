using System.Globalization;
using Capsule.Diagnostics;

namespace Capsule.Runtime;

/// <summary>
/// The sink the host installs at boot. Every level goes to standard output, so lines stay in the
/// order the game wrote them, each prefixed with the simulation tick it was written on.
/// </summary>
internal sealed class ConsoleLogSink : ILogSink
{
    /// <summary>Reads the current tick; null before the host's clock exists.</summary>
    internal Func<long>? Tick { get; set; }

    public void Write(LogLevel level, string message)
    {
        string line = Format(level, Tick?.Invoke(), message);

        // A failed write must never take the game down with it: a shell launched with its
        // standard output closed still runs.
        try
        {
            Console.Out.WriteLine(line);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // The tick column is the same width either way, so lines from before the clock existed still
    // line up with the rest.
    internal static string Format(LogLevel level, long? tick, string message)
    {
        string label = level switch
        {
            LogLevel.Debug => "debug",
            LogLevel.Warning => "warn ",
            LogLevel.Error => "error",
            _ => "info ",
        };

        return tick is { } current
            ? string.Create(CultureInfo.InvariantCulture, $"[{current,7}] {label} {message}")
            : $"[   boot] {label} {message}";
    }
}
