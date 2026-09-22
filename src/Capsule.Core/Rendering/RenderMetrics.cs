namespace Capsule.Rendering;

/// <summary>
/// Render-command counts for the last rewrite of a <see cref="FrameView"/>. One command is one sprite,
/// and a run of text costs one command per glyph.
/// </summary>
/// <param name="Submitted">Sprites and lines offered to the culler.</param>
/// <param name="Visible">Sprites and lines the culler kept.</param>
/// <param name="Lights">Lights the frame will draw this frame, already culled.</param>
public readonly record struct RenderMetrics(int Submitted, int Visible, int Lights = 0)
{
    /// <summary>Commands offered that the camera rejected.</summary>
    public int Culled => Submitted - Visible;
}
