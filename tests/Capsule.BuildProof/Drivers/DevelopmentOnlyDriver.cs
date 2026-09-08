using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;

namespace Capsule.BuildProof.Drivers;

// Under a marked directory: compiled and registered by an ordinary build, absent from a shipped
// one. BuildProof.targets asserts both.
internal sealed class DevelopmentOnlyDriver : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        return false;
    }
}
