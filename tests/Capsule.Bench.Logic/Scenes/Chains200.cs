using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>200 swaying roots, each 64 entities deep with a collider at the bottom: moving a deep hierarchy and notifying it down to its collider.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Chains200 : Scene
{
    public Chains200()
    {
        Camera = new ParkedCamera();

        for (int index = 0; index < 200; index++)
        {
            Add(new Chain(new Vector2(16f + ((index % 20) * 30f), 8f + ((index / 20) * 34f)), index * 7));
        }
    }
}
