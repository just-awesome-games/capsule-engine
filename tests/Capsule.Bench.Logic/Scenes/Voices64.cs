using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>64 looping voices across two buses, each rewriting volume and pan every step: the mixer's per-step command generation, heard by nothing.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Voices64 : Scene
{
    public Voices64()
    {
        Camera = new ParkedCamera();

        for (int index = 0; index < 64; index++)
        {
            Add(new Hummer(new Vector2(20f + ((index % 16) * 40f), 40f + ((index / 16) * 80f)), index));
        }
    }
}
