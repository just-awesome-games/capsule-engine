using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>10 000 animated sprites at mixed speeds, each reading its clip's events: the animator's step and its event reports alone.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Animators10k : Scene
{
    public Animators10k()
    {
        Camera = new ParkedCamera();

        Vector2 extent = World.ViewportSize - new Vector2(16f, 24f);
        for (int index = 0; index < 10_000; index++)
        {
            Add(new Dancer(new Vector2(8f + ((index * 7) % extent.X), 24f + ((index * 13) % extent.Y)), index));
        }
    }
}
