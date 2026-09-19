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
/// Where log lines go. The host installs one before the simulation runs. A game implements it to
/// capture output in its own tests.
/// </summary>
public interface ILogSink
{
    /// <summary>
    /// Writes one line, on the simulation thread and synchronously. A sink that throws is detached,
    /// and everything it would have received afterwards is lost.
    /// </summary>
    void Write(LogLevel level, string message);
}

/// <summary>
/// How game logic says something out loud. It is write-only telemetry, so installing a sink does not
/// change the state a run reaches. Logging is silent until a sink is installed, which the runtime does
/// at boot. A sink that throws is detached, and its line and everything after it are lost.
/// </summary>
public static class Log
{
    // Private, because a reader would let a game call a sink directly and bypass the containment below,
    // and a presence query would let a game branch on how the host was configured.
    private static ILogSink? Sink { get; set; }

    /// <summary>Installs <paramref name="sink"/>, replacing whatever was there. Null silences logging.</summary>
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

    /// <summary>Writes one line at <paramref name="level"/>. A null message writes as an empty one.</summary>
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
            // Telemetry does not decide whether a step completes, and a sink that failed once will fail on
            // every line after it.
            if (ReferenceEquals(Sink, sink))
            {
                Sink = null;
            }
        }
    }
}
