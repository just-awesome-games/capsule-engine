using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>200 overlapping sensors of 96 by 96 on a 20 by 10 grid over the ledges of <c>triggers-200.scene.json</c>, with 1 000 actors drifting through the whole room: contact reporting and its per-step settle.</summary>
/// <remarks>
/// Row <c>Triggers200Stacked</c> stacks the sensors on one square, with the actors drifting through a
/// 192 by 192 corner around it: contact reporting with each sensor holding hundreds of contacts. A
/// sensor's contact buffers double when it meets more at once than ever before, and those rare steps
/// are the one allocation, by design.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
[Workload(WorkloadKind.Simulation, Row = Stacked)]
[Workload(WorkloadKind.Paced, Row = Stacked)]
public sealed class Triggers200 : Scene
{
    private const string Stacked = "Triggers200Stacked";

    public Triggers200(SceneContent content)
        : base(content)
    {
        bool stacked = Workloads.Running == Stacked;
        Vector2 room = FindSingle<TileMap>().Size;
        Vector2 extent = stacked ? new Vector2(192f, 192f) : room;
        Camera = new ParkedCamera(extent / 2f);

        for (int index = 0; index < 200; index++)
        {
            Vector2 zone = stacked ? extent / 2f : new Vector2((index % 20) * 64f, (index / 20) * 72f);
            Add(new Zone(zone, new Vector2(96f, 96f)));
        }

        // An actor turns back a body's width short of the area's far edges.
        Vector2 bounds = extent - new Vector2(8f, 8f);
        for (int index = 0; index < 1_000; index++)
        {
            Vector2 position = new((index * 37) % bounds.X, (index * 53) % bounds.Y);
            Vector2 velocity = new(((index % 6) - 2.5f) * 0.6f, ((index % 5) - 2) * 0.75f);
            Add(new Crosser(position, velocity, bounds));
        }
    }
}
