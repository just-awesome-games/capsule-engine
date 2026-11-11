using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 1 000 sweeping beams on a 40 by 25 grid, each a triangle whose additive fill and contact-reporting sensor take
/// one outline every step and close to a line once a sweep, over 1 000 actors drifting through: the polygon path,
/// and a reshaped trigger's proxy moves, collapses and settle.
/// </summary>
/// <remarks>
/// A sensor's contact buffers double when it meets more at once than ever before, and those rare steps are the one
/// allocation, by design.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
[Workload(WorkloadKind.Rendering)]
public sealed class Beams1k : Scene
{
    public Beams1k()
    {
        Vector2 room = World.ViewportSize;
        Camera = new ParkedCamera();

        for (int index = 0; index < Workloads.Scaled(1_000); index++)
        {
            Add(new Beam(new Vector2((index % 40) * 16f, (index / 40) * 14f), index * 7));
        }

        // An actor turns back a body's width short of the room's far edges.
        Vector2 bounds = room - new Vector2(8f, 8f);
        for (int index = 0; index < Workloads.Scaled(1_000); index++)
        {
            Vector2 position = new((index * 37) % bounds.X, (index * 53) % bounds.Y);
            Vector2 velocity = new(((index % 6) - 2.5f) * 0.6f, ((index % 5) - 2) * 0.75f);
            Add(new Crosser(position, velocity, bounds));
        }
    }
}
