using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.UI;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>320 menu rows in boxes nested three deep, one caption resized every step: the layout cache's re-solve and every screen draw's slot read.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Containers : Scene
{
    public Containers()
    {
        Camera = new ParkedCamera();
        Add(new RowColumns());
    }
}
