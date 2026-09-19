using System.Numerics;

namespace Capsule.Physics;

/// <summary>What one swept move actually did.</summary>
/// <param name="Translation">
/// The translation applied, the requested one clipped on each axis by whatever stopped it. Add it to
/// the mover's position.
/// </param>
/// <param name="BlockedX">Whether something stopped the move short along X.</param>
/// <param name="BlockedY">Whether something stopped the move short along Y.</param>
/// <param name="ContactCount">
/// How many surfaces the move reached. The caller's span holds as many as fit and the rest are
/// counted only.
/// </param>
/// <param name="ContactsAlongX">
/// How many contacts in the caller's span the X sweep wrote. They come first in the span and the
/// remainder belong to the Y sweep. Read a contact against the blocked flag of its own sweep.
/// </param>
public readonly record struct MoveResult2D(
    Vector2 Translation,
    bool BlockedX,
    bool BlockedY,
    int ContactCount,
    int ContactsAlongX);
