using System.Globalization;

namespace Capsule.Scenes;

/// <summary>
/// Writes named columns into a <see cref="StateTrace"/> for one subject on one tick. Handed to an
/// <see cref="ITraceSource"/> for the length of that call and valid no longer.
/// </summary>
public readonly struct TraceWriter
{
    private readonly StateTrace? _trace;
    private readonly string? _subject;
    private readonly long _tick;

    internal TraceWriter(StateTrace trace, string subject, long tick)
    {
        _trace = trace;
        _subject = subject;
        _tick = tick;
    }

    /// <summary>Writes <paramref name="value"/> under <paramref name="column"/> verbatim.</summary>
    /// <exception cref="ArgumentException"><paramref name="column"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public void Write(string column, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentNullException.ThrowIfNull(value);

        // Default-constructed: the writer belongs to no trace, so there is nothing to write to.
        _trace?.Add(_tick, _subject!, column, value);
    }

    /// <summary>Writes <paramref name="value"/> in the shortest text that round-trips it.</summary>
    /// <exception cref="ArgumentException"><paramref name="column"/> is null or blank.</exception>
    public void Write(string column, float value) => Write(column, StateTrace.Text(value));

    /// <summary>Writes <paramref name="value"/> in the invariant culture.</summary>
    /// <exception cref="ArgumentException"><paramref name="column"/> is null or blank.</exception>
    public void Write(string column, int value) => Write(column, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Writes <paramref name="value"/> as <c>true</c> or <c>false</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="column"/> is null or blank.</exception>
    public void Write(string column, bool value) => Write(column, value ? "true" : "false");
}
