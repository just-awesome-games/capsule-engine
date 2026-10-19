using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>About 2 000 pooled bullets a second fired across the pillared room of <c>bullets-2k.scene.json</c>, each reporting contacts until a wall ends it: collider registration churn and per-collider contact tracking.</summary>
/// <remarks>
/// Row <c>Bullets2kStacked</c> fires at a tenth of the speed, its 2 000 live bullets crowding a ball 60
/// pixels across that no wall reaches: collider churn and contact tracking where every new collider
/// lands among all the others. Row <c>Bullets2kTargeted</c> is the stacked ball with a <see cref="Target"/> at its
/// centre: the same churn where a collider on another layer detects every bullet.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
[Workload(WorkloadKind.Simulation, Row = Stacked)]
[Workload(WorkloadKind.Paced, Row = Stacked)]
[Workload(WorkloadKind.Simulation, Row = Targeted)]
[Workload(WorkloadKind.Paced, Row = Targeted)]
public sealed class Bullets2k : Scene
{
    private const string Stacked = "Bullets2kStacked";
    private const string Targeted = "Bullets2kTargeted";

    public Bullets2k(SceneContent content)
        : base(content)
    {
        Vector2 centre = FindSingle<TileMap>().Size / 2f;
        Camera = new ParkedCamera(centre);

        // The gun's speed is in pixels a step.
        bool stacked = Workloads.Running is Stacked or Targeted;
        Add(new Gun(centre, stacked ? 0.5f : 5f));
        if (Workloads.Running == Targeted)
        {
            Add(new Target(centre));
        }
    }
}
