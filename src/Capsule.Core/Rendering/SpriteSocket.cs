using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// A named point on one frame, in the texel space its <see cref="Sprite.Pivot"/> is in: where a
/// muzzle, a hand or a hitpoint sits on that drawing. A frame carries the sockets its sheet set on
/// it; <c>SpriteRenderer.Socket</c> is what places an entity at one.
/// </summary>
/// <param name="Name">The socket's name, as the sheet declared it; the key a renderer binds by.</param>
/// <param name="Point">The point, in texels of the frame's region from its top-left corner.</param>
public readonly record struct SpriteSocket(string Name, Vector2 Point);
