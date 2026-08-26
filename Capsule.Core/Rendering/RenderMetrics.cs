namespace Capsule.Rendering;

/// <summary>Quad counts for the last rewrite of a <see cref="FrameView"/>.</summary>
public readonly record struct RenderMetrics(int TotalQuads, int VisibleQuads)
{
    public int CulledQuads => TotalQuads - VisibleQuads;
}
