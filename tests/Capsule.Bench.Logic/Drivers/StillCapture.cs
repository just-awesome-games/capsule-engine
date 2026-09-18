using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Drivers;

/// <summary>Presses nothing; captures the frame at step 30, once the run has settled, and ends it at step 90. The suite renames the capture per label.</summary>
public sealed class StillCapture : IInputDriver
{
    public const string Path = "artifacts/bench/still.png";

    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;

        if (tick == 30)
        {
            scene.Run.CaptureFrame(Path);
        }
        else if (tick == 90)
        {
            scene.Run.RequestExit();
        }

        return true;
    }
}
