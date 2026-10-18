using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>The <c>Bullets2kStacked</c> row's ball of 2 000 slow bullets with no room around it, overlapped whole once a step: an all-hits query returning every collider in a crowd.</summary>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
public sealed class Overlap2k : Scene
{
    public Overlap2k()
    {
        Vector2 centre = World.ViewportSize / 2f;
        Camera = new ParkedCamera(centre);
        Add(new Gun(centre, 0.5f));
        Add(new Sweeper(centre, new Vector2(80f, 80f)));
    }
}
