using Capsule.Maps;
using Capsule.Scenes.Spawning;

namespace Capsule.Scenes;

/// <summary>
/// Everything a scene composed from a map is built out of, as one value: a subclass takes what it
/// is handed and passes it on unchanged, so what a map scene needs can grow without every scene
/// class changing shape.
/// </summary>
/// <param name="Map">The map to compose.</param>
/// <param name="Entities">What each spawn type constructs.</param>
public readonly record struct MapSceneContext(Map Map, EntityRegistry Entities);
