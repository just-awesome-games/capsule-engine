namespace Capsule.Scenes.Documents;

/// <summary>
/// One placed entity in a scene document. The id is stable for the life of the document and never
/// reused; X and Y are in world pixels, carried through from the authoring source unchanged.
/// </summary>
public readonly record struct EntityPlacement(int Id, string Type, float X, float Y);
