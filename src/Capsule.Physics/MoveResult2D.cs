using System.Numerics;

namespace Capsule.Physics;

/// <summary>What one swept move actually did.</summary>
/// <param name="Translation">
/// The translation applied, the requested one cut short and slid along whatever stopped it. Add it to
/// the mover's position.
/// </param>
/// <param name="Blocked">Whether something stopped any part of the move short.</param>
/// <param name="ContactCount">
/// How many surfaces the move reached. The caller's span holds as many as fit and the rest are
/// counted only.
/// </param>
public readonly record struct MoveResult2D(Vector2 Translation, bool Blocked, int ContactCount);
