using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>About 2 000 pooled adds and removes a second: deferred structural changes, start and stop, and notifier settling.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Churn2k : Scene
{
    public Churn2k()
    {
        Camera = new ParkedCamera();
        Add(new Spawner());
    }
}
