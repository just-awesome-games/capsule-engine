using System.ComponentModel;

namespace Capsule.Scenes.Spawning;

/// <summary>
/// One spawnable entity as an <see cref="EntityRegistry"/> entry: the document type it answers to
/// and what constructs it.
/// </summary>
/// <param name="SpawnType">The key a scene document's entries name it by.</param>
/// <param name="Spawner">What constructs it from a placement.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct EntityRegistration(
    string SpawnType,
    EntitySpawner Spawner);
