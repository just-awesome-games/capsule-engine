using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// A world-space viewport. The renderer interpolates its centres, preserves <see cref="Size"/>'s
/// aspect ratio and letterboxes any slack; a non-positive size draws nothing.
/// </summary>
/// <param name="PreviousCenter">The centre as of the previous fixed step.</param>
/// <param name="Center">The centre as of the current fixed step.</param>
/// <param name="Size">World units the viewport spans.</param>
public readonly record struct CameraView(Vector2 PreviousCenter, Vector2 Center, Vector2 Size)
{
    /// <summary>A view that does not interpolate: previous centre and current are the same point.</summary>
    public CameraView(Vector2 center, Vector2 size)
        : this(center, center, size)
    {
    }

    /// <summary>The union of the previous and current viewport regions, used for culling.</summary>
    public ViewBounds SweptBounds
    {
        get
        {
            Vector2 halfSize = Size / 2f;

            return new ViewBounds(
                MathF.Min(PreviousCenter.X, Center.X) - halfSize.X,
                MathF.Min(PreviousCenter.Y, Center.Y) - halfSize.Y,
                MathF.Max(PreviousCenter.X, Center.X) + halfSize.X,
                MathF.Max(PreviousCenter.Y, Center.Y) + halfSize.Y);
        }
    }
}
