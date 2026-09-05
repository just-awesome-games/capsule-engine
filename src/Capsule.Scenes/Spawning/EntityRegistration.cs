using System.ComponentModel;

namespace Capsule.Scenes.Spawning;

/// <summary>
/// One spawnable entity as an <see cref="EntityRegistry"/> entry: the document type it answers to,
/// what constructs it, and the textures a scene spawning it must keep resident.
/// </summary>
/// <param name="SpawnType">The key a scene document's entries name it by.</param>
/// <param name="Spawner">What constructs it from a placement.</param>
/// <param name="Textures">
/// The residency groups the build derived from the code this entity reaches, or null when it
/// reaches none.
/// </param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct EntityRegistration(
    string SpawnType,
    EntitySpawner Spawner,
    TextureSetBuilder? Textures = null);
