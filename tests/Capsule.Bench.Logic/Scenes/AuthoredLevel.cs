using Capsule.Bench.Logic.Cameras;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// The level of <c>authored-level.scene.json</c>: four 384 by 96 tile layers that each author one 1,024-entry
/// palette, one of them colliding, and 2,000 placed sensors with five authored members each. Read under the
/// <c>Startup</c> driver, it is what composing a big authored level costs the first time.
/// </summary>
[Workload(WorkloadKind.Simulation)]
public sealed class AuthoredLevel : Scene
{
    public AuthoredLevel(SceneContent content)
        : base(content) =>
        Camera = new ParkedCamera();
}
