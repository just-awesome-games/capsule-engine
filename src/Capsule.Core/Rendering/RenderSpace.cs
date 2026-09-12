namespace Capsule.Rendering;

/// <summary>Which of a frame's two ordered intent lists something is drawn on.</summary>
public enum RenderSpace
{
    /// <summary>
    /// World units, placed by the camera and culled against it. The default, and drawn first.
    /// </summary>
    World,

    /// <summary>
    /// Canvas pixels from the canvas's top-left corner, Y-down, indifferent to the camera and culled
    /// against <see cref="FrameView.Canvas"/>. Drawn over the whole world layer.
    /// </summary>
    Screen,
}
