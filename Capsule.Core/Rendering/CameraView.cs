using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// The world region the viewport shows: <see cref="Center"/> is the world point at the
/// centre of the viewport and <see cref="Size"/> how many world units it spans. A world
/// unit has no intrinsic size; this is the only thing that maps one to pixels. Aspect is
/// not preserved — a viewport whose ratio differs from <see cref="Size"/>'s stretches.
/// The default value spans nothing, and nothing is drawn through it.
/// </summary>
public readonly record struct CameraView(Vector2 Center, Vector2 Size);
