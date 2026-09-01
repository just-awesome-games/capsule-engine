using Capsule.Diagnostics;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class LogTests : IDisposable
{
    private readonly ILogSink? _installed = Log.Sink;

    public void Dispose() => Log.UseSink(_installed);

    [Fact]
    public void Log_IsSilentUntilASinkIsInstalled()
    {
        Log.UseSink(null);

        Assert.False(Log.IsEnabled);

        // The contract is that this is not an error: a headless run logs into nothing.
        Log.Info("nobody is listening");
        Log.Warning("still nobody");
        Log.Error("nor now");
    }

    [Fact]
    public void Log_HandsEveryLevelToTheInstalledSinkInOrder()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        Log.Info("first");
        Log.Warning("second");
        Log.Error("third");

        Assert.Equal(
            [
                new LogEntry(LogLevel.Info, "first"),
                new LogEntry(LogLevel.Warning, "second"),
                new LogEntry(LogLevel.Error, "third"),
            ],
            sink.Entries);
    }

    [Fact]
    public void Log_ReadsANullMessageAsAnEmptyLineRatherThanThrowing()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        Log.Info(null);

        Assert.Equal(new LogEntry(LogLevel.Info, string.Empty), Assert.Single(sink.Entries));
    }

    // A sink is telemetry. Letting one end a step would mean a run with logging installed reaching
    // a different state from the same run without it, which is the one thing Log promises not to do.
    [Fact]
    public void ASinkThatThrows_IsDetachedRatherThanAllowedToEndTheStep()
    {
        ThrowingLogSink sink = new();
        Log.UseSink(sink);

        Log.Info("the line that breaks it");

        Assert.Equal(1, sink.Attempts);
        Assert.Null(Log.Sink);
        Assert.False(Log.IsEnabled);

        // Nothing reaches it again, so a sink that fails once cannot go on failing.
        Log.Info("and the one after");
        Log.Warning("and another");

        Assert.Equal(1, sink.Attempts);
    }

    [Fact]
    public void ASinkThatDoesNotThrow_IsLeftAloneByTheContainment()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        Log.Info("first");
        Log.Info("second");

        Assert.Same(sink, Log.Sink);
        Assert.Equal(2, sink.Entries.Count);
    }

    [Fact]
    public void UseSink_ReplacesWhateverWasListening()
    {
        CollectingLogSink first = new();
        CollectingLogSink second = new();

        Log.UseSink(first);
        Log.Info("to the first");
        Log.UseSink(second);
        Log.Info("to the second");

        Assert.Single(first.Entries);
        Assert.Single(second.Entries);
        Assert.Same(second, Log.Sink);
    }

    // The console format is documented for people reading their game's output, so it is a contract.
    [Theory]
    [InlineData(LogLevel.Info, 0L, "[      0] info  ready")]
    [InlineData(LogLevel.Warning, 1234L, "[   1234] warn  ready")]
    [InlineData(LogLevel.Error, 9_999_999L, "[9999999] error ready")]
    public void TheConsoleSink_PrefixesEveryLineWithItsTickAndLevel(LogLevel level, long tick, string expected) =>
        Assert.Equal(expected, ConsoleLogSink.Format(level, tick, "ready"));

    [Fact]
    public void TheConsoleSink_MarksALineWrittenBeforeTheClockExists()
    {
        string line = ConsoleLogSink.Format(LogLevel.Info, null, "ready");

        Assert.Equal("[   boot] info  ready", line);
        Assert.Equal(ConsoleLogSink.Format(LogLevel.Info, 0L, "ready").Length, line.Length);
    }

    [Fact]
    public void Clear_ForgetsWhatASinkCollected()
    {
        CollectingLogSink sink = new();
        sink.Write(LogLevel.Info, "something");

        sink.Clear();

        Assert.Empty(sink.Entries);
    }

    private sealed class ThrowingLogSink : ILogSink
    {
        internal int Attempts { get; private set; }

        public void Write(LogLevel level, string message)
        {
            Attempts++;

            throw new InvalidOperationException("this sink is broken");
        }
    }
}
