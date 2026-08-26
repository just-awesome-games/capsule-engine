using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// The world region the viewport shows: <see cref="Center"/> is the world point at the
/// centre of the viewport and <see cref="Size"/> how many world units it spans. A world
/// unit has no intrinsic size; this is the only thing that maps one to pixels. Aspect is
/// preserved — a viewport whose ratio differs from <see cref="Size"/>'s shows the region
/// scaled uniformly, centred, with black bars over the slack, never stretched.
/// The default value spans nothing, and nothing is drawn through it.
/// </summary>
public readonly record struct CameraView(Vector2 Center, Vector2 Size);
