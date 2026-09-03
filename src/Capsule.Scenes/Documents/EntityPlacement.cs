namespace Capsule.Scenes.Documents;

/// <summary>
/// One game-defined entity entry in a scene document. The id is stable for the life of the
/// document and never reused; X and Y are authored world-space coordinates.
/// </summary>
/// <param name="Id">The entry's identity in the document's one id space.</param>
/// <param name="Type">The spawn type claimed by a game entity.</param>
/// <param name="X">The authored world-space X coordinate.</param>
/// <param name="Y">The authored world-space Y coordinate.</param>
/// <param name="ScaleX">The authored X scale factor; 1 is the authored size unscaled.</param>
/// <param name="ScaleY">The authored Y scale factor; 1 is the authored size unscaled.</param>
public readonly record struct EntityPlacement(
    int Id,
    string Type,
    float X,
    float Y,
    float ScaleX = 1f,
    float ScaleY = 1f);
