using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;

namespace Capsule.BuildProof.Incremental.Drivers;

// Under a marked directory, and the only thing in this project that changes between the two
// directions: whether the registry names it is whether the toggle recompiled.
internal sealed class DevelopmentOnlyDriver : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        return false;
    }
}
