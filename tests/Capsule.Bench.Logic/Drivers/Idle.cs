namespace Capsule.Bench.Logic.Drivers;

/// <summary>Presses nothing and never ends the run, which the paced lane's frame budget ends.</summary>
public sealed class Idle : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;

        return true;
    }
}
