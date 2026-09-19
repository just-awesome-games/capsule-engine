using System.Globalization;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Runtime;

namespace Capsule.Tests.Diagnostics;

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

    // WithLogSink documents the shape for people reading their game's output, so the tick, the
    // level and the message are a contract; the columns they are padded into are not.
    [Theory]
    [InlineData(LogLevel.Debug, 0L, "debug")]
    [InlineData(LogLevel.Info, 0L, "info")]
    [InlineData(LogLevel.Warning, 1234L, "warn")]
    [InlineData(LogLevel.Error, 9_999_999L, "error")]
    public void TheConsoleSink_PrefixesEveryLineWithItsTickAndLevel(LogLevel level, long tick, string name)
    {
        string line = ConsoleLogSink.Format(level, tick, "ready");

        Assert.StartsWith("[", line, StringComparison.Ordinal);
        Assert.Contains(tick.ToString(CultureInfo.InvariantCulture), line, StringComparison.Ordinal);
        Assert.Contains(name, line, StringComparison.Ordinal);
        Assert.EndsWith(" ready", line, StringComparison.Ordinal);
    }

    // Every line is the same width whatever its tick, so the columns read straight down.
    [Fact]
    public void TheConsoleSink_MarksALineWrittenBeforeTheClockExists()
    {
        string line = ConsoleLogSink.Format(LogLevel.Info, null, "ready");

        Assert.Contains("boot", line, StringComparison.Ordinal);
        Assert.Equal(ConsoleLogSink.Format(LogLevel.Info, 0L, "ready").Length, line.Length);
    }

    // The tick column is only worth reading if a headless run fills it in as the windowed one does.
    [Fact]
    public void AHeadlessRun_PrefixesALineASceneWritesFromAStepWithThatStepsTick()
    {
        TextWriter output = Console.Out;
        using StringWriter captured = new();
        Console.SetOut(captured);

        try
        {
            CapsuleEngine.Configure(
                    "Logging Game",
                    new DesktopPlatform(),
                    new SceneRegistry(
                        new EntityRegistry([]),
                        [SceneRegistration.Plain(typeof(Announcing), static _ => new Announcing())]))
                .WithFixedStep(10)
                .WithoutCrashLog()
                .RunHeadless<Announcing>(new InputScript().Wait(4).Build());

            // The clock left with the run: a line written afterwards is not the last step's.
            Log.Info("after");
        }
        finally
        {
            Console.SetOut(output);
        }

        string[] lines = captured.ToString().Split(Environment.NewLine);
        Assert.Contains("[      2] info  stepped", lines);
        Assert.Contains("[   boot] info  after", lines);
    }

    private sealed class Announcing : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (context.Tick == 2)
            {
                Log.Info("stepped");
            }
        }
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
