using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>300 drifting sensors of 96 by 96 in a crowd of 400 drifting actors, all inside one 128 by 128 square: contact settling where every reporter holds hundreds of contacts, some entering and leaving each step, and both sides of a pair move.</summary>
/// <remarks>
/// Row <c>Seekers300Respawn</c> takes each sensor out of the world and back once a second, five a
/// step, as pooled sensors despawned and spawned again in the crowd would: a reporter's whole crowd
/// arriving in one step. A returning sensor keeps the buffers it grew, and allocates no more than one
/// that never left.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = Respawn)]
public sealed class Seekers300 : Scene
{
    private const string Respawn = "Seekers300Respawn";

    public Seekers300()
    {
        int period = Workloads.Running == Respawn ? 60 : 0;
        Vector2 extent = new(128f, 128f);
        Camera = new ParkedCamera(extent / 2f);

        for (int index = 0; index < Workloads.Scaled(300); index++)
        {
            Vector2 position = new((index * 29) % extent.X, (index * 43) % extent.Y);
            Vector2 velocity = new(((index % 7) - 3f) * 0.4f, ((index % 4) - 1.5f) * 0.5f);
            Add(new Seeker(position, velocity, new Vector2(96f, 96f), extent, period, index % 60));
        }

        Vector2 bounds = extent - new Vector2(8f, 8f);
        for (int index = 0; index < Workloads.Scaled(400); index++)
        {
            Vector2 position = new((index * 37) % bounds.X, (index * 53) % bounds.Y);
            Vector2 velocity = new(((index % 6) - 2.5f) * 0.6f, ((index % 5) - 2) * 0.75f);
            Add(new Crosser(position, velocity, bounds));
        }
    }
}
