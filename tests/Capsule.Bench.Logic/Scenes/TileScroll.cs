using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>The 1024 by 256 cavern of <c>tile-scroll.scene.json</c> under a looping camera, so the visible set changes every step: tile culling, emission and submission.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class TileScroll : Scene
{
    public TileScroll(SceneContent content)
        : base(content) =>
        Camera = new LoopCamera(new Vector2(400f, 1200f), new Vector2(15_000f, 1_600f));
}
