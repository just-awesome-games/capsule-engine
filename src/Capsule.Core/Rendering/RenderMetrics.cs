namespace Capsule.Rendering;

/// <summary>Render-command counts for the last rewrite of a <see cref="FrameView"/>.</summary>
/// <remarks>
/// One command is one sprite or line. Text, a panel and a tiling count one command per sprite they
/// expand to.
/// </remarks>
/// <param name="Submitted">Sprites and lines offered to the culler.</param>
/// <param name="Visible">Sprites and lines the culler kept.</param>
/// <param name="Lights">Lights the frame will draw this frame, already culled.</param>
public readonly record struct RenderMetrics(int Submitted, int Visible, int Lights = 0)
{
    /// <summary>Commands offered that the culler rejected, against the camera or the canvas.</summary>
    public int Culled => Submitted - Visible;
}
