using Capsule.Maps;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>The map and entity registry used to construct a <see cref="MapScene"/>.</summary>
/// <param name="Map">The map to compose.</param>
/// <param name="Entities">What each spawn type constructs.</param>
public readonly record struct MapSceneContext(Map Map, EntityRegistry Entities);
