using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;

namespace Capsule.BuildProof.AbsoluteDrivers;

// Under a marked directory the project includes by absolute path rather than through the SDK's
// relative glob: the removal matches resolved locations, so this leaves a shipping compile exactly
// as Drivers/DevelopmentOnlyDriver.cs does.
internal sealed class AbsoluteDriver : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        return false;
    }
}
