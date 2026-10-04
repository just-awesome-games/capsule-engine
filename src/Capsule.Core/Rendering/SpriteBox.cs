namespace Capsule.Rendering;

/// <summary>
/// A named rect on one frame, in the texel space its <see cref="Sprite.Pivot"/> uses, marking where a
/// hurtbox, a hitbox or a spike strip sits on that drawing. A frame carries the boxes its sheet set on
/// it in its <see cref="Sprite.Marks"/>.
/// </summary>
/// <param name="Name">The box's name, unique within its frame.</param>
/// <param name="Area">Texels from the frame region's top-left corner. It may reach outside the region.</param>
public readonly record struct SpriteBox(string Name, Rect Area);
