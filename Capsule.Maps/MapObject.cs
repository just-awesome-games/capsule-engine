namespace Capsule.Maps;

/// <summary>
/// One placed thing on a map. The id is stable for the life of the map and never reused;
/// X and Y are in map pixel space, carried through from the authoring source unchanged.
/// </summary>
public readonly record struct MapObject(int Id, string Type, float X, float Y);
