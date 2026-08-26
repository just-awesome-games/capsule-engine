using System.Numerics;

namespace Capsule.Scenes.Spawning;

/// <summary>
/// One level entity's data, as its <c>[LevelType]</c> class receives it. <see cref="Position"/>
/// is the raw level coordinate: what it anchors is an authoring convention, so translating it to
/// the entity's own anchor belongs in that entity's constructor.
/// </summary>
public readonly record struct EntitySpawn(int Id, string Type, Vector2 Position);
