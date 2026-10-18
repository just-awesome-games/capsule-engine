using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>64 swinging one-way platforms on an 8 by 8 grid in the room of <c>platforms-64.scene.json</c>, each carrying 16 pacing riders: carrying, shoving and riding bodies on moving colliders.</summary>
/// <remarks>
/// Row <c>Platforms64Stacked</c> swings every platform about one home just above the floor: carrying,
/// shoving and riding with every platform's move reaching a crowd. A platform's rider list doubles when
/// more stand on it than ever before, and those rare steps are the one allocation, by design.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
[Workload(WorkloadKind.Simulation, Row = Stacked)]
[Workload(WorkloadKind.Paced, Row = Stacked)]
public sealed class Platforms64 : Scene
{
    private const string Stacked = "Platforms64Stacked";

    private const int RidersEach = 16;

    public Platforms64(SceneContent content)
        : base(content)
    {
        Vector2 room = FindSingle<TileMap>().Size;
        bool stacked = Workloads.Running == Stacked;

        // The stacked home sits low enough that riders who step off land in reach of the swing, and high enough that it never meets the floor's.
        Vector2 home = new(room.X / 2f, room.Y - World.TileSize - 44f);
        Camera = new ParkedCamera(stacked ? home : room / 2f);

        // Up-and-down and side-to-side swings alternate, each at its own phase.
        for (int index = 0; index < 64; index++)
        {
            Vector2 start = stacked ? home : new Vector2(64f + ((index % 8) * 150f), 60f + ((index / 8) * 84f));
            Shuttle shuttle = new(start, index % 2 == 0, index * 17);
            Add(shuttle);

            // Riders stand spread along the slab where its phase starts it, a pace clear of its ends.
            Vector2 slab = shuttle.Position;
            for (int rider = 0; rider < RidersEach; rider++)
            {
                Add(new Rider(slab + new Vector2(10f + (rider * 2.75f), -12f), (index * RidersEach) + rider));
            }
        }
    }
}
