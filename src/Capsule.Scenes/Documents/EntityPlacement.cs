using System.Numerics;

namespace Capsule.Scenes.Documents;

/// <summary>
/// One game-defined entity entry in a scene document. Its id is stable for the life of the document and is
/// never reused.
/// </summary>
/// <param name="Id">The entry's id in the document's single id space.</param>
/// <param name="Type">The spawn type a game entity claimed.</param>
/// <param name="X">The authored world-space X coordinate.</param>
/// <param name="Y">The authored world-space Y coordinate.</param>
/// <param name="ScaleX">The authored X scale factor, where 1 is the authored size.</param>
/// <param name="ScaleY">The authored Y scale factor, where 1 is the authored size.</param>
/// <param name="ZIndex">
/// The authored draw band, or null when the placement authors none. It reaches the entity's constructor as
/// <see cref="Spawning.EntitySpawn.ZIndex"/>, which applies it before the constructor's body runs.
/// </param>
/// <param name="ScrollFactor">
/// The authored scroll factor, or null when the placement authors none. It reaches the entity's constructor as
/// <see cref="Spawning.EntitySpawn.ScrollFactor"/>, which applies it before the constructor's body runs.
/// </param>
public readonly record struct EntityPlacement(
    int Id,
    string Type,
    float X,
    float Y,
    float ScaleX = 1f,
    float ScaleY = 1f,
    int? ZIndex = null,
    Vector2? ScrollFactor = null);
