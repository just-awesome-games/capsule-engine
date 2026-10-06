using System.Numerics;

namespace Capsule.UI;

/// <summary>A pointer held down on a <see cref="Focusable"/>, in canvas pixels.</summary>
/// <param name="Start">Where the click pressed the item.</param>
/// <param name="Position">Where the pointer is now, which may lie outside the item.</param>
public readonly record struct PointerDrag(Vector2 Start, Vector2 Position);
