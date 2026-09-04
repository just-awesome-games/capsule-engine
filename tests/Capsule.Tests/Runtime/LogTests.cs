using Capsule.Diagnostics;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class LogTests : IDisposable
{
    // Log's sink is write-only, so a test cannot put back what it found; silent is where a
    // headless run starts.
    public void Dispose() => Log.UseSink(null);

    [Fact]
    public void Log_IsSilentUntilASinkIsInstalled()
    {
        Log.UseSink(null);

        // The contract is that this is not an error: a headless run logs into nothing.
        Log.Write(LogLevel.Debug, "nobody is listening");
        Log.Info("nor now");
        Log.Warning("still nobody");
        Log.Error("nor now either");

        // Nor was any of it held for whoever listens next.
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void Log_HandsEveryLevelToTheInstalledSinkInOrder()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        Log.Write(LogLevel.Debug, "first");
        Log.Info("second");
        Log.Warning("third");
        Log.Error("fourth");

        Assert.Equal(
            [
                new LogEntry(LogLevel.Debug, "first"),
                new LogEntry(LogLevel.Info, "second"),
                new LogEntry(LogLevel.Warning, "third"),
                new LogEntry(LogLevel.Error, "fourth"),
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

    // A sink is telemetry: a run with logging installed must reach the same state as one without.
    [Fact]
    public void ASinkThatThrows_IsDetachedRatherThanAllowedToEndTheStep()
    {
        ThrowingLogSink sink = new();
        Log.UseSink(sink);

        Log.Info("the line that breaks it");

        Assert.Equal(1, sink.Attempts);

        // Nothing reaches it again, so a sink that fails once cannot go on failing.
        Log.Info("and the one after");
        Log.Warning("and another");

        Assert.Equal(1, sink.Attempts);

        // The seam itself survives the detachment: the next sink is listened to.
        CollectingLogSink replacement = new();
        Log.UseSink(replacement);
        Log.Info("to the replacement");

        Assert.Single(replacement.Entries);
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
    }

    // The console format is documented for people reading their game's output, so it is a contract.
    [Theory]
    [InlineData(LogLevel.Debug, 0L, "[      0] debug ready")]
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
