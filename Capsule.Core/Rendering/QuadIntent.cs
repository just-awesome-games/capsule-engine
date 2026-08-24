using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One axis-aligned rectangle to draw. World units, Y-down: <see cref="Position"/> is
/// the top-left corner and <see cref="Size"/> the extent from it.
/// <see cref="PreviousPosition"/> is the corner as of the previous fixed step, and the
/// renderer interpolates the two by the frame alpha. A quad on its first step sets both
/// to the same value, or it slides in from nowhere.
/// </summary>
public readonly record struct QuadIntent(
    Vector2 PreviousPosition,
    Vector2 Position,
    Vector2 Size,
    ColorRgba Color);
