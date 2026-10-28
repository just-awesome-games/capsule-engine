using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Scenes;
using static Capsule.Tests.DevTools.RecordingFixtures;

namespace Capsule.Tests.DevTools;

// --capture draws on a GPU, which the suite does not assume. What it refuses before the run, and before
// any device opens, needs none.
[Collection(LogSinkCollection.Name)]
public sealed class RunCaptureTests : IDisposable
{
    private readonly TempWorkspace _workspace = new("capture");

    public void Dispose() => _workspace.Dispose();

    // A recording knows its last tick, and an open range over it is counted to that tick before the run.
    [Fact]
    public void AnOpenRangeOverARecording_IsCountedToItsEnd_AndRefusedBeforeTheRun()
    {
        string recording = _workspace.PathTo("run.recording");
        using (RecordedRun run = new(Builder()))
        {
            for (int step = 0; step < RunCapture.Ceiling + 1; step++)
            {
                run.Step(DeviceSnapshot.Empty);
            }

            using FileStream file = File.Create(recording);
            run.Recorder.Write(file, crashed: false);
        }

        string frames = _workspace.PathTo("frames");
        CommandLineException refused = Assert.Throws<CommandLineException>(
            () => Builder().WithCommandLine(["--replay", recording, "--headless", "--capture", frames, "0.."]));

        Assert.StartsWith("--capture selects 602 ticks, past its ceiling of 600.", refused.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(frames));
    }

    // A device that will not open is refused before the run, naming the flag that needed it.
    [Fact]
    public void AGpuThatWillNotOpen_IsRefusedBeforeTheRun()
    {
        EngineBuilder builder = Builder()
            .WithInputDriver(new Idle())
            .WithCommandLine(["--headless", "--capture", _workspace.PathTo("frames"), "0", "--gpu-driver", "nowhere"]);

        CommandLineException refused = Assert.Throws<CommandLineException>(() => builder.RunScene<Reading>());

        Assert.StartsWith("--capture could not open a GPU to draw on: ", refused.Message, StringComparison.Ordinal);
    }

    private sealed class Idle : IInputDriver
    {
        public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
        {
            snapshot = DeviceSnapshot.Empty;

            return tick < 1;
        }
    }
}
