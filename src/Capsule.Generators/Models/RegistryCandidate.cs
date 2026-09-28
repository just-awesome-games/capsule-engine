namespace Capsule.Generators;

/// <summary>
/// What one type declaration was read as. A declaration can be a scene and an input driver at once,
/// so all four are described from a single symbol binding.
/// </summary>
/// <param name="Scene">
/// Every Scene-deriving class, abstract included. A document's own key can compose it, or its
/// baseScene key can name it, and the scene resolver filters which at the point of use.
/// </param>
internal readonly record struct RegistryCandidate(
    EntityModel? Entity,
    SceneModel? Scene,
    InputDriverModel? Driver,
    CameraModel? Camera)
{
    internal bool IsEmpty => Entity is null && Scene is null && Driver is null && Camera is null;
}
