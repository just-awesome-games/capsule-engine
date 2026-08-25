namespace Capsule.Levels;

/// <summary>
/// One placed thing in a level. The id is stable for the life of the level and never reused;
/// X and Y are in map pixel space, carried through from the authoring source unchanged.
/// </summary>
public readonly record struct LevelEntity(int Id, string Type, float X, float Y);
