using System.Numerics;

namespace Capsule.Scenes.Spawning;

/// <summary>
/// One authored placement, as the entity it spawns receives it. <see cref="Position"/> is the raw
/// authored coordinate: what it anchors is an authoring convention, so translating it to the
/// entity's own anchor belongs in that entity's constructor.
/// </summary>
/// <param name="Id">The placement's identity in the document's one id space.</param>
/// <param name="Type">The spawn type the entity claimed.</param>
/// <param name="Position">The raw authored coordinate.</param>
/// <param name="Scale">
/// The raw authored scale factors, positive and finite on both axes. What they mean is the
/// entity's constructor's decision — a <see cref="SpriteRenderer.Scale"/>, a collider shape run
/// through <see cref="Collision.Shape2D.Scaled"/>, or nothing at all.
/// </param>
public readonly record struct EntitySpawn(int Id, string Type, Vector2 Position, Vector2 Scale)
{
    /// <summary>The same placement at the authored size, which is the common case.</summary>
    public EntitySpawn(int id, string type, Vector2 position)
        : this(id, type, position, Vector2.One)
    {
    }
}
