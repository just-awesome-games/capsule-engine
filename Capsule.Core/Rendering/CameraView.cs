using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// The world region the viewport shows: <see cref="Center"/> is the world point at the
/// centre of the viewport and <see cref="Size"/> how many world units it spans. A world
/// unit has no intrinsic size; this is the only thing that maps one to pixels. Aspect is
/// preserved — a viewport whose ratio differs from <see cref="Size"/>'s shows the region
/// scaled uniformly, centred, with black bars over the slack, never stretched.
/// <see cref="PreviousCenter"/> is the centre as of the previous fixed step, and the renderer
/// interpolates the two by the frame alpha exactly as it does a quad's corner, so the world
/// and the camera move on the same clock. A camera that cut rather than moved sets both to the
/// same value, or the cut renders as a sweep.
/// The default value spans nothing, and nothing is drawn through it.
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

    /// <summary>
    /// The world rect the viewport covers at any alpha this step: the previous and current
    /// regions unioned. Culling tests this rather than the settled region, so a quad the camera
    /// sweeps over mid-step survives to be drawn — the same conservative-and-exact test a quad's
    /// own swept corner already gets.
    /// </summary>
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
