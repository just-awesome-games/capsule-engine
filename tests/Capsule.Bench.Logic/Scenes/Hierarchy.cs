using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>2 000 gimbals, each three nested arms, turning and breathing every step: parented transform recomposition and composed child sprites.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Hierarchy : Scene
{
    public Hierarchy()
    {
        Camera = new ParkedCamera();

        for (int index = 0; index < 2_000; index++)
        {
            Add(new Gimbal(new Vector2(6f + ((index % 50) * 12.8f), 6f + ((index / 50) * 9f)), index));
        }
    }
}
