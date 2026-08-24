namespace Capsule.Rendering;

/// <summary>
/// Everything a simulation wants drawn. Immutable, so a simulation builds one per
/// distinct visual state and holds it; the render path then allocates nothing.
/// A view rebuilt every frame is a defect, not a style choice.
/// Render intent members arrive with the first renderable feature.
/// </summary>
public sealed class FrameView
{
    /// <summary>Nothing to draw.</summary>
    public static FrameView Empty { get; } = new();
}
