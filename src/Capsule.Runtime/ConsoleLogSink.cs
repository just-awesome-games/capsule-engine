using System.Globalization;
using Capsule.Diagnostics;

namespace Capsule.Runtime;

/// <summary>
/// The sink the host installs at boot. Every level goes to standard output in write order, each
/// line prefixed with the simulation tick it was written on.
/// </summary>
internal sealed class ConsoleLogSink : ILogSink
{
    /// <summary>Reads the current tick; null before the host's clock exists.</summary>
    internal Func<long>? Tick { get; set; }

    public void Write(LogLevel level, string message)
    {
        string line = Format(level, Tick?.Invoke(), message);

        // A shell launched with its standard output closed still runs.
        try
        {
            Console.Out.WriteLine(line);
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException)
        {
        }
    }

    // The tick column is the same width either way, so lines from before the clock line up.
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
