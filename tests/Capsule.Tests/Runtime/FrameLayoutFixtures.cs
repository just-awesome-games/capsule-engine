using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Runtime;

internal static class FrameLayoutFixtures
{
    /// <summary>A point-sampled view of a camera at the origin, sized to the canvas unless given a size.</summary>
    internal static FrameView View(Vector2 canvas, Vector2? cameraSize = null, ViewportFit fit = ViewportFit.Letterbox) => new()
    {
        Canvas = canvas,
        Sampling = TextureSampling.Point,
        Camera = new CameraView(Vector2.Zero, Vector2.Zero, cameraSize ?? canvas, fit),
    };

    internal static ScreenLayout Layout((int Width, int Height)? resolution, FrameView view, int outputWidth, int outputHeight) =>
        FrameLayout.Layout(resolution, view.Camera, view.Canvas, outputWidth, outputHeight);
}
