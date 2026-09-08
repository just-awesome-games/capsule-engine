#pragma warning disable IDE0130 // Its directory is the assertion, so the folder name cannot match a namespace.

using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;

namespace Capsule.BuildProof.CaseVariant;

// The marked half of the case-variant pair: an ordinary build compiles and registers it, a shipped
// one holds neither. Without it the unmarked sibling's survival would prove nothing, since a marker
// that was never found would leave both in place.
internal sealed class CaseVariantDevDriver : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        return false;
    }
}
