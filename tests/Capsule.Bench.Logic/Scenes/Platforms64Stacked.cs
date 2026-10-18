using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary><see cref="Platforms64"/>'s 64 one-way platforms swinging about one home just above the floor of the same room, each loaded with 16 pacing riders: carrying, shoving and riding with every platform's move reaching a crowd. A platform's rider list doubles when more stand on it than ever before, and those rare steps are the one allocation, by design.</summary>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
public sealed class Platforms64Stacked : Scene
{
    // The room's extent in tiles, as the document authors it.
    private const int TilesWide = 80;

    private const int TilesHigh = 45;

    private const int RidersEach = 16;

    public Platforms64Stacked(SceneContent content)
        : base(content)
    {
        // Low enough that riders who step off land in reach of the swing, high enough that it never meets the floor's.
        Vector2 home = new(TilesWide * World.TileSize / 2f, ((TilesHigh - 1) * World.TileSize) - 44f);
        Camera = new ParkedCamera(home);

        // Up-and-down and side-to-side swings alternate, each at its own phase about the shared home.
        for (int index = 0; index < 64; index++)
        {
            Shuttle shuttle = new(home, index % 2 == 0, index * 17);
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
