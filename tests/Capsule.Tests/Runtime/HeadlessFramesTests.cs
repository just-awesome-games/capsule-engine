using System.Diagnostics;
using System.Globalization;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
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

        EngineBuilder builder = CapsuleEngine.Configure(
                "Paced Game",
                new DesktopPlatform(),
                new SceneRegistry(new EntityRegistry([]), [SceneRegistration.Plain(typeof(SceneFixtures.Idle), _ => new SceneFixtures.Idle())]))
            .WithFixedStep(100)
            .WithMaxStepsPerFrame(3)
            .WithFrameDiagnostics(path)
            .WithoutCrashLog()
            .WithoutLogging();
        builder.Timestamp = clock.Read;
        builder.RunHeadless<SceneFixtures.Idle>(new Overrunning(clock));

        string[] lines = File.ReadAllLines(path);
        int header = Array.IndexOf(lines, "intervalMs,updateMs,drawMs,steps,gen0");
        int[] steps = [.. lines[(header + 1)..].Select(static row => int.Parse(row.Split(',')[3], CultureInfo.InvariantCulture))];

        Assert.Equal(["builderEntered", "hostConstructed", "firstUpdate", "firstDraw"], lines[1..header].Select(static line => line[2..].Split(',')[0]));
        Assert.Contains(1, steps);
        Assert.All(steps, static count => Assert.InRange(count, 0, 1));
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

        internal long Read() => _timestamp += Stopwatch.Frequency / 1_000_000;

        internal void Advance(double seconds) => _timestamp += (long)(seconds * Stopwatch.Frequency);
    }
}
