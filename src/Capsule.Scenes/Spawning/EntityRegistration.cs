using System.ComponentModel;

namespace Capsule.Scenes.Spawning;

/// <summary>
/// One <see cref="EntityRegistry"/> entry: the document type an entity answers to and the delegate that
/// constructs it.
/// </summary>
/// <param name="SpawnType">The key a scene document's entries name it by.</param>
/// <param name="Spawner">The delegate that constructs it from a placement.</param>
/// <param name="Apply">
/// The delegate that sets its authorable members, deferring entity references until every entry is constructed,
/// or null when it declares none.
/// </param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct EntityRegistration(
    string SpawnType,
    EntitySpawner Spawner,
    EntityApplier? Apply = null);
