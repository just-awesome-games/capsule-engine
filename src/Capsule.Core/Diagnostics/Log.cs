namespace Capsule.Diagnostics;

/// <summary>How much attention a log line is asking for.</summary>
public enum LogLevel
{
    /// <summary>Ordinary running commentary.</summary>
    Info,

    /// <summary>Something is off but the game carries on.</summary>
    Warning,

    /// <summary>Something went wrong.</summary>
    Error,
}

/// <summary>
/// Where log lines go. The host installs one before the simulation runs; a game never implements
/// this except to capture output in its own tests.
/// </summary>
public interface ILogSink
{
    /// <summary>
    /// Writes one line. Called on the simulation thread, synchronously. A sink is expected not to
    /// throw: one that does is detached, and everything it would have received afterwards is lost.
    /// </summary>
    /// <param name="level">How much attention the line is asking for.</param>
    /// <param name="message">The line, already formatted; never null.</param>
    void Write(LogLevel level, string message);
}

/// <summary>
/// How game logic says something out loud. Write-only telemetry: nothing here feeds back into
/// simulation state, and a run with a sink installed produces the same state transitions as a run
/// without one, so logging never weakens the determinism contract.
/// <para>
/// That holds even of a sink that misbehaves. A sink is expected not to throw, and one that does is
/// detached rather than allowed to end the step: the exception reaches nobody, the line is lost,
/// and so is everything that sink would have received afterwards.
/// </para>
/// <para>
/// Silent until a sink is installed, which the runtime does at boot; a headless harness installs
/// its own or leaves it silent.
/// </para>
/// </summary>
public static class Log
{
    /// <summary>The sink installed, or null while nothing is listening.</summary>
    public static ILogSink? Sink { get; private set; }

    /// <summary>Whether anything is listening; test it before building a message that costs something.</summary>
    public static bool IsEnabled => Sink is not null;

    /// <summary>Installs <paramref name="sink"/>, replacing whatever was there; null silences logging.</summary>
    public static void UseSink(ILogSink? sink) => Sink = sink;

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
            // Telemetry does not get to decide whether a step completes, which is the whole of the
            // claim above: a run with a sink has to reach the same state as a run without one, and
            // a sink that threw through here would end the step instead. Dropped rather than left
            // installed, because a sink that failed once fails on every line after it and the throw
            // would only be contained again and again.
            if (ReferenceEquals(Sink, sink))
            {
                Sink = null;
            }
        }
    }
}
