using System.Numerics;

namespace Capsule.Scenes.Spawning;

/// <summary>
/// One authored placement, as the entity it spawns receives it. <see cref="Position"/> is the raw
/// authored coordinate; translating it to the entity's own anchor belongs in that constructor.
/// The <see cref="Entity(EntitySpawn)"/> constructor applies <see cref="ZIndex"/> and
/// <see cref="ScrollFactor"/> before the derived constructor's body runs, so the class's own writes
/// win over the document's.
/// </summary>
/// <param name="Id">The placement's identity in the document's one id space.</param>
/// <param name="Type">The spawn type the entity claimed.</param>
/// <param name="Position">The raw authored coordinate.</param>
/// <param name="Scale">
/// The raw authored scale factors, positive and finite on both axes. What they mean is the
/// entity's constructor's decision — the entity's own <see cref="Entity.Scale"/>, a collider
/// shape run through <see cref="Capsule.Physics.Shape2D.Scaled"/>, or nothing at all.
/// </param>
/// <param name="ZIndex">The authored draw band, or null where the placement authors none.</param>
/// <param name="ScrollFactor">The authored scroll factor, or null where the placement authors none.</param>
public readonly record struct EntitySpawn(
    int Id,
    string Type,
    Vector2 Position,
    Vector2 Scale,
    int? ZIndex = null,
    Vector2? ScrollFactor = null)
{
    /// <summary>The same placement at the authored size, which is the common case.</summary>
    public EntitySpawn(int id, string type, Vector2 position)
        : this(id, type, position, Vector2.One)
    {
    }
}
