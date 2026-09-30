using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>2 000 drifting actors of six components each: the per-component bookkeeping of every step and every move.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Actors2k : Scene
{
    public Actors2k()
    {
        Camera = new ParkedCamera();

        Vector2 extent = World.ViewportSize - new Vector2(12f, 24f);
        for (int index = 0; index < 2_000; index++)
        {
            Vector2 position = new((index * 7) % extent.X, (index * 13) % extent.Y);
            Vector2 velocity = new(((index % 6) - 2.5f) * 0.4f, ((index % 5) - 2) * 0.5f);
            Add(new Actor(position, velocity, index));
        }
    }
}
