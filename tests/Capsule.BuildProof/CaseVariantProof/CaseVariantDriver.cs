// The template for the case-variant proof, copied to drivers/ on a case-sensitive filesystem and
// compiled from there. It is never compiled where it sits: see BuildProof.targets.
#pragma warning disable IDE0130 // Its directory is the assertion, so the folder name cannot match a namespace.

using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;

namespace Capsule.BuildProof.CaseVariantDrivers;

// In drivers/, an unmarked sibling of the marked Drivers/ differing from it only by case. Every
// build compiles and registers it: a removal that folded case would take it for marked.
internal sealed class CaseVariantDriver : IInputDriver
{
    public bool TryNext(Scene scene, long tick, out DeviceSnapshot snapshot)
    {
        snapshot = DeviceSnapshot.Empty;
        return false;
    }
}
