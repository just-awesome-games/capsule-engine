using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.AotSmoke.Logic.Drivers;

// Under a marked directory, so an ordinary build compiles and registers it and a publish has
// neither the source nor the registration. The published smoke asserts the second half.
internal sealed class DevelopmentOnlyDriver : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;

        return false;
    }
}
