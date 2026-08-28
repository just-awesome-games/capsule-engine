using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// An axis-aligned, Y-down world rectangle. The renderer interpolates its top-left corner from
/// <see cref="PreviousPosition"/> to <see cref="Position"/>.
/// </summary>
public readonly record struct QuadIntent(
    Vector2 PreviousPosition,
    Vector2 Position,
    Vector2 Size,
    ColorRgba Color);
