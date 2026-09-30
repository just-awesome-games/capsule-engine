using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>64 swinging one-way platforms in the room of <c>platforms-64.scene.json</c>, each carrying 16 pacing riders: carrying, shoving and riding bodies on moving colliders.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Platforms64 : Scene
{
    // The room's extent in tiles, as the document authors it.
    private const int TilesWide = 80;

    private const int TilesHigh = 45;

    private const int RidersEach = 16;

    public Platforms64(SceneContent content)
        : base(content)
    {
        Vector2 room = new(TilesWide * World.TileSize, TilesHigh * World.TileSize);
        Camera = new ParkedCamera(room / 2f);

        // An 8 by 8 grid at a 150 by 84 pitch, alternating up-and-down and side-to-side swings.
        for (int index = 0; index < 64; index++)
        {
            Vector2 home = new(64f + ((index % 8) * 150f), 60f + ((index / 8) * 84f));
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
