using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;

namespace Capsule.BuildProof.Incremental;

// The control: an ordinary driver outside any marked directory, which every build compiles and
// every build registers.
internal sealed class KeptDriver : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        return false;
    }
}
