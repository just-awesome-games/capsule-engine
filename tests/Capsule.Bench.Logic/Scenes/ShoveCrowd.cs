using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>600 bobbing crates piled on one spot, each with a sensor box, and one body they move standing clear: every crate's move searches for bodies to shove through 1 200 colliders.</summary>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
public sealed class ShoveCrowd : Scene
{
    private const int Crates = 600;

    public ShoveCrowd()
    {
        Vector2 pile = World.ViewportSize / 2f;
        Camera = new ParkedCamera(pile);

        for (int index = 0; index < Crates; index++)
        {
            Add(new Bobber(pile, index * 7));
        }

        Add(new Onlooker(pile - new Vector2(200f, 0f)));
    }
}
