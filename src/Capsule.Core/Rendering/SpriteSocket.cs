using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// A named point on one frame, in the texel space its <see cref="Sprite.Pivot"/> uses, marking where a
/// muzzle, a hand or a hitpoint sits on that drawing. A frame carries the sockets its sheet set on it,
/// and <c>SpriteRenderer.Socket</c> places an entity at one.
/// </summary>
public readonly record struct SpriteSocket(string Name, Vector2 Point);
