namespace Capsule.Diagnostics;

/// <summary>One line a <see cref="CollectingLogSink"/> kept.</summary>
public readonly record struct LogEntry(LogLevel Level, string Message);

/// <summary>A sink that keeps what it is given, for a headless harness to assert on. It grows without bound until <see cref="Clear"/>.</summary>
public sealed class CollectingLogSink : ILogSink
{
    private readonly List<LogEntry> _entries = [];

    /// <summary>Every line written since the last <see cref="Clear"/>, in order.</summary>
    public IReadOnlyList<LogEntry> Entries => _entries;

    /// <inheritdoc/>
    public void Write(LogLevel level, string message) => _entries.Add(new LogEntry(level, message));

    /// <summary>Forgets everything written so far.</summary>
    public void Clear() => _entries.Clear();
}
