namespace Capsule.Generators;

/// <summary>What one type declaration was read as, with every domain described from one symbol binding.</summary>
/// <remarks>A declaration can be a scene and an input driver at once.</remarks>
/// <param name="Scene">
/// Every Scene-deriving class, abstract included. A document's own key can compose it, or its
/// baseScene key can name it, and the scene resolver filters which at the point of use.
/// </param>
internal readonly record struct RegistryCandidate(
    EntityModel? Entity,
    SceneModel? Scene,
    InputDriverModel? Driver,
    ScenePayloadModel? Payload)
{
    internal bool IsEmpty => Entity is null && Scene is null && Driver is null && Payload is null;
}
