using System.Diagnostics;

namespace Capsule.Diagnostics;

/// <summary>How much attention a log line is asking for.</summary>
public enum LogLevel
{
    /// <summary>Detail for whoever is working on the code, below ordinary commentary.</summary>
    Debug,

    /// <summary>Ordinary running commentary.</summary>
    Info,

    /// <summary>Something is off but the game carries on.</summary>
    Warning,

    /// <summary>Something went wrong.</summary>
    Error,
}

/// <summary>
/// Where log lines go. The host installs one before the simulation runs; a game implements it only
/// to capture output in its own tests.
/// </summary>
public interface ILogSink
{
    /// <summary>
    /// Writes one line, on the simulation thread and synchronously. A sink that throws is detached,
    /// and everything it would have received afterwards is lost.
    /// </summary>
    /// <param name="level">How much attention the line is asking for.</param>
    /// <param name="message">The line, already formatted; never null.</param>
    void Write(LogLevel level, string message);
}

/// <summary>
/// How game logic says something out loud. Write-only telemetry: nothing reads back, so a run with
/// a sink installed reaches the same state as a run without one. A sink that throws is detached
/// rather than allowed to end the step; its line and everything after it are lost.
/// <para>
/// Silent until a sink is installed, which the runtime does at boot; a headless harness installs
/// its own or leaves it silent.
/// </para>
/// </summary>
public static class Log
{
    // Private: a reader would let a game call a sink directly, past the containment below, and a
    // presence query would let one branch on how the host was configured.
    private static ILogSink? Sink { get; set; }

    /// <summary>Installs <paramref name="sink"/>, replacing whatever was there; null silences logging.</summary>
    public static void UseSink(ILogSink? sink) => Sink = sink;

    /// <summary>
    /// Writes one line of detail for whoever is working on the code. The call and its message
    /// expression are both compiled out of a Release build of the calling assembly.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Debug(string? message) => Write(LogLevel.Debug, message);

    /// <summary>Writes one line of running commentary.</summary>
    public static void Info(string? message) => Write(LogLevel.Info, message);

    /// <summary>Writes one line about something that is off but not fatal.</summary>
    public static void Warning(string? message) => Write(LogLevel.Warning, message);

    /// <summary>Writes one line about something that went wrong.</summary>
    public static void Error(string? message) => Write(LogLevel.Error, message);

    /// <summary>Writes one line at <paramref name="level"/>.</summary>
    /// <param name="level">How much attention the line is asking for.</param>
    /// <param name="message">The line; a null reads as an empty one, because a log call is never worth an exception.</param>
    public static void Write(LogLevel level, string? message)
    {
        if (Sink is not { } sink)
        {
            return;
        }

        try
        {
            sink.Write(level, message ?? string.Empty);
        }
        catch
        {
            // Telemetry does not get to decide whether a step completes. Dropped rather than left
            // installed: a sink that failed once fails on every line after it.
            if (ReferenceEquals(Sink, sink))
            {
                Sink = null;
            }
        }
    }
}
