using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>The eight tiled layers of <c>parallax-8.scene.json</c> at eight scroll factors under a looping camera: scrolled runs, their virtual cameras, tiling and snapping.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class Parallax8 : Scene
{
    public Parallax8(SceneContent content)
        : base(content) =>
        Camera = new LoopCamera(new Vector2(320f, 180f), new Vector2(4_000f, 60f));
}
