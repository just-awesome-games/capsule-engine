namespace Capsule.Rendering;

/// <summary>
/// Render-command counts for the last rewrite of a <see cref="FrameView"/>, across every kind.
/// </summary>
public readonly record struct RenderMetrics(int Submitted, int Visible)
{
    /// <summary>Commands offered that the camera rejected.</summary>
    public int Culled => Submitted - Visible;
}
