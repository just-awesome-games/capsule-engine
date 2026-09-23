using System.Numerics;

namespace Capsule.Scenes.Spawning;

/// <summary>One authored placement as the entity it spawns receives it.</summary>
/// <remarks>
/// <see cref="Position"/> is the raw authored coordinate, and the entity's constructor translates it to
/// that entity's own anchor. The <see cref="Entity(EntitySpawn)"/> constructor applies
/// <see cref="ZIndex"/> and <see cref="ScrollFactor"/> before the derived constructor's body runs.
/// Writes in that body override the document.
/// </remarks>
/// <param name="Id">The placement's id in the document's single id space.</param>
/// <param name="Type">The spawn type the entity claimed.</param>
/// <param name="Position">The raw authored coordinate.</param>
/// <param name="Scale">
/// The raw authored scale factors, positive and finite on both axes. The entity's constructor decides what
/// they mean: the entity's own <see cref="Entity.Scale"/>, a collider shape run through
/// <see cref="Capsule.Physics.Shape2D.Scaled"/>, or nothing.
/// </param>
/// <param name="ZIndex">The authored draw band, or null when the placement authors none.</param>
/// <param name="ScrollFactor">The authored scroll factor, or null when the placement authors none.</param>
public readonly record struct EntitySpawn(
    int Id,
    string Type,
    Vector2 Position,
    Vector2 Scale,
    int? ZIndex = null,
    Vector2? ScrollFactor = null)
{
    /// <summary>A placement at scale one.</summary>
    public EntitySpawn(int id, string type, Vector2 position)
        : this(id, type, position, Vector2.One)
    {
    }
}
