using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary><see cref="Bullets2k"/>'s gun firing at a tenth of the speed in the same room, its 2 000 live bullets crowding a ball 60 pixels across that no wall reaches: collider registration churn and contact tracking where every new collider lands among all the others.</summary>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
public sealed class Bullets2kStacked : Scene
{
    // The room's extent in tiles, as the document authors it.
    private const int TilesWide = 40;

    private const int TilesHigh = 23;

    public Bullets2kStacked(SceneContent content)
        : base(content)
    {
        Vector2 centre = new(TilesWide * World.TileSize / 2f, TilesHigh * World.TileSize / 2f);
        Camera = new ParkedCamera(centre);
        Add(new Gun(centre, 0.5f));
    }
}
