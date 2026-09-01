using System.Numerics;

namespace Capsule.Collision;

/// <summary>What one swept move actually did.</summary>
/// <param name="Translation">
/// The translation that was applied, which is the requested one clipped on each axis by whatever
/// stopped it. Adding it to the mover's position is the move.
/// </param>
/// <param name="BlockedX">Whether something stopped the move short along X.</param>
/// <param name="BlockedY">Whether something stopped the move short along Y.</param>
/// <param name="ContactCount">
/// How many contacts were written into the caller's span; never more than that span holds, even
/// when more were touched.
/// </param>
/// <param name="XContactCount">
/// How many of those contacts the X sweep wrote. They come first, so the rest are the Y sweep's:
/// this is what says which axis's blocked flag judges a given contact.
/// </param>
public readonly record struct MoveResult2D(
    Vector2 Translation,
    bool BlockedX,
    bool BlockedY,
    int ContactCount,
    int XContactCount);
