using Capsule.Bench.Logic.Cameras;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>The prober's queries against the room-scale corridor of <c>queries.scene.json</c>: the collision world alone.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Queries : Scene
{
    public Queries(SceneContent content)
        : base(content) =>
        Camera = new ParkedCamera();
}
