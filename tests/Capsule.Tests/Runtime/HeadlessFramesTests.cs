using System.Diagnostics;
using System.Globalization;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class HeadlessFramesTests
{
    // A headless run under frame diagnostics runs real-time frames. Steps costing more than their
    // length leave each frame owing more, and a frame runs one and drops the rest well short of the
    // step bound. The trace leaves out the device and media stages a headless run never crosses.
    [Fact]
    public void AHeadlessRunsFrames_SlowingDownUnderOverrunningSteps()
    {
        using TempWorkspace workspace = new("capsule-headless-frames-");
        string path = workspace.PathTo("frames.csv");
        Clock clock = new();

        Builder(path, clock).RunHeadless<SceneFixtures.Idle>(new Overrunning(clock));

        string[] lines = File.ReadAllLines(path);
        int header = Array.IndexOf(lines, Header);
        int[] steps = [.. Rows(lines).Select(static row => int.Parse(row[5], CultureInfo.InvariantCulture))];

        Assert.Equal(["builderEntered", "hostConstructed", "firstUpdate", "firstDraw"], lines[1..header].Select(static line => line[2..].Split(',')[0]));
        Assert.Contains(1, steps);
        Assert.All(steps, static count => Assert.InRange(count, 0, 1));
    }

    // Uncapped, the frames run back to back, one step each, and none waits out the step's period. The
    // clock moves a microsecond a read, and a paced frame would read it thousands of times waiting.
    [Fact]
    public void AnUncappedHeadlessRun_StepsOnceAFrameWithoutWaiting()
    {
        using TempWorkspace workspace = new("capsule-headless-frames-");
        string path = workspace.PathTo("frames.csv");
        Clock clock = new();

        Builder(path, clock).WithCommandLine(["--uncapped"]).RunHeadless<SceneFixtures.Idle>(new Free());

        string[][] rows = [.. Rows(File.ReadAllLines(path))];

        // The last frame asks the driver for a thirteenth step, which it declines, and steps nothing.
        Assert.Equal(Enumerable.Range(1, 12).Append(12).Select(static tick => tick.ToString(CultureInfo.InvariantCulture)), rows.Select(static row => row[0]));
        Assert.Equal(Enumerable.Repeat("1", 12).Append("0"), rows.Select(static row => row[5]));
        Assert.InRange(clock.Reads, 1, 1_000);
    }

    private const string Header = "tick,intervalMs,updateMs,viewMs,drawMs,steps,gen0,bytes,processBytes,heapBytes";

    // A 100 Hz step and a bound of three steps a frame, timing frames to path on clock.
    private static EngineBuilder Builder(string path, Clock clock)
    {
        EngineBuilder builder = CapsuleEngine.Configure(
                "Paced Game",
                new SceneRegistry(new EntityRegistry([]), [SceneRegistration.Plain(typeof(SceneFixtures.Idle), _ => new SceneFixtures.Idle())]))
            .WithFixedStep(100)
            .WithMaxStepsPerFrame(3)
            .WithFrameDiagnostics(path)
            .WithoutCrashLog()
            .WithoutLogging();
        builder.Timestamp = clock.Read;

        return builder;
    }

    private static IEnumerable<string[]> Rows(string[] lines) =>
        lines.SkipWhile(static line => line != Header).Skip(1).Select(static row => row.Split(','));

    // Twelve steps that cost nothing.
    private sealed class Free : IInputDriver
    {
        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            snapshot = DeviceSnapshot.Empty;

            return tick < 12;
        }
    }

    // Each step costs two and a half times the 10 ms step, and the run ends after twelve.
    private sealed class Overrunning(Clock clock) : IInputDriver
    {
        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            snapshot = DeviceSnapshot.Empty;
            clock.Advance(0.025);

            return tick < 12;
        }
    }

    // Moves a microsecond on every read, which lets a frame's wait for its period end, and further
    // when a step spends time.
    private sealed class Clock
    {
        private long _timestamp = Stopwatch.GetTimestamp();

        internal int Reads { get; private set; }

        internal long Read()
        {
            Reads++;

            return _timestamp += Stopwatch.Frequency / 1_000_000;
        }

        internal void Advance(double seconds) => _timestamp += (long)(seconds * Stopwatch.Frequency);
    }
}
