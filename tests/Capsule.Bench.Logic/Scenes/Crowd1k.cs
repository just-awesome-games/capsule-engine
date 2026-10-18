using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>1 000 walkers on the walled tile ring of <c>crowd-1k.scene.json</c>: the entity pipeline, bodies and broadphase at that density.</summary>
/// <remarks>
/// Row <c>Crowd1kStacked</c> drops the walkers on the room's centre as two packs that cross and turn
/// together: the entity pipeline, bodies and broadphase with every body inside every other's box.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
[Workload(WorkloadKind.Simulation, Row = Stacked)]
[Workload(WorkloadKind.Paced, Row = Stacked)]
public sealed class Crowd1k : Scene
{
    private const string Stacked = "Crowd1kStacked";

    public Crowd1k(SceneContent content)
        : base(content)
    {
        TileMap room = FindSingle<TileMap>();
        Camera = new ParkedCamera(room.Size / 2f);
        bool stacked = Workloads.Running == Stacked;

        for (int index = 0; index < 1_000; index++)
        {
            Vector2 position = stacked
                ? room.Size / 2f
                : new Vector2(
                    World.TileSize + ((index * 7) % ((room.Width - 2) * World.TileSize)),
                    World.TileSize + ((index * 13) % ((room.Height - 3) * World.TileSize)));
            Add(new Walker(position, index));
        }
    }
}
