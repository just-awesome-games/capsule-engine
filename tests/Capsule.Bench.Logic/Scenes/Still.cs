using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.UI;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// One frame that never changes, so its capture cannot depend on the wall-clock alpha and two
/// builds of the renderer compare byte for byte. <c>still.scene.json</c> places the world in draw
/// order; the screen layer is added here because a document cannot place a screen entity. Together
/// they cover every path the renderer has; <c>--driver StillCapture</c> saves the frame.
/// </summary>
[Workload(WorkloadKind.Rendering)]
public sealed class Still : Scene
{
    public Still(SceneContent content)
        : base(content)
    {
        Camera = new ParkedCamera();
        ClearColor = new ColorRgba(24, 28, 40);

        Add(new Caption(Anchor.TopLeft, new Vector2(8f, 8f), "Capsule bench 0123 AaBb"));
        Add(new Panel(Anchor.BottomRight, new Vector2(-130f, -60f), new Vector2(120f, 48f)));
    }
}
