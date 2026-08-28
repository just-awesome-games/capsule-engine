namespace Capsule.Rendering;

/// <summary>Quad counts for the last rewrite of a <see cref="FrameView"/>.</summary>
public readonly record struct RenderMetrics(int TotalQuads, int VisibleQuads)
{
    /// <summary>Quads offered that the camera rejected.</summary>
    public int CulledQuads => TotalQuads - VisibleQuads;
}
