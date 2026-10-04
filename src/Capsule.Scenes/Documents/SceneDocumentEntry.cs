using System.Numerics;
using System.Text.Json;

namespace Capsule.Scenes.Documents;

/// <summary>One entry in a scene document's ordered list: the entity it places and the members it authors.</summary>
/// <param name="Type">The spawn type of the entity class the entry composes, <c>tile-map</c> for the engine's map.</param>
/// <param name="X">The authored world-space X coordinate, 0 by default.</param>
/// <param name="Y">The authored world-space Y coordinate, 0 by default.</param>
/// <param name="ScaleX">The authored X scale factor, where 1 is the authored size.</param>
/// <param name="ScaleY">The authored Y scale factor, where 1 is the authored size.</param>
/// <param name="ZIndex">
/// The authored draw band, or null when the entry authors none. It reaches the entity's constructor as
/// <see cref="Spawning.EntitySpawn.ZIndex"/>, and the base constructor applies it before the subclass body
/// runs.
/// </param>
/// <param name="ScrollFactor">
/// The authored scroll factor, or null when the entry authors none. It reaches the entity's constructor as
/// <see cref="Spawning.EntitySpawn.ScrollFactor"/>, and the base constructor applies it before the subclass
/// body runs.
/// </param>
/// <param name="RotationDegrees">
/// The authored turn in degrees, clockwise on screen, where 0 is unturned. It reaches the entity's
/// constructor in radians as <see cref="Spawning.EntitySpawn.Rotation"/>.
/// </param>
/// <param name="Properties">
/// The entry's authorable member values as one JSON object, or null when it authors none. Each key sets the
/// claiming class's authorable member of that name, camel-cased.
/// </param>
public readonly record struct SceneDocumentEntry(
    string Type,
    float X = 0f,
    float Y = 0f,
    float ScaleX = 1f,
    float ScaleY = 1f,
    int? ZIndex = null,
    Vector2? ScrollFactor = null,
    float RotationDegrees = 0f,
    JsonElement? Properties = null)
{
    /// <summary>The id an entity reference names this entry by, or null when nothing references it.</summary>
    public int? Id { get; init; }
}
