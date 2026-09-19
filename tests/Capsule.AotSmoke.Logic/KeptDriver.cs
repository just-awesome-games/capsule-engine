using Capsule.Input;
using Capsule.Scenes;

namespace Capsule.AotSmoke.Logic;

// The control: a driver outside any marked directory, which every build compiles and registers.
// Without it a marker that excluded everything would leave the shipping assertions passing.
internal sealed class KeptDriver : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;

        return false;
    }
}
