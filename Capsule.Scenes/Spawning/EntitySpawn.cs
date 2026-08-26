using System.Numerics;

namespace Capsule.Scenes.Spawning;

/// <summary>
/// One authored placement, as the entity it spawns receives it. <see cref="Position"/> is the raw
/// authored coordinate: what it anchors is an authoring convention, so translating it to the
/// entity's own anchor belongs in that entity's constructor.
/// </summary>
public readonly record struct EntitySpawn(int Id, string Type, Vector2 Position);
