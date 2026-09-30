using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>1 000 walkers with a step height of 6 over the 4 and 6 unit lips of <c>steps-1k.scene.json</c>: grounded bodies stepping up onto and down off ledges.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Steps1k : Scene
{
    // The room's extent in tiles, as the document authors it.
    private const int TilesWide = 34;

    private const int TilesHigh = 14;

    public Steps1k(SceneContent content)
        : base(content)
    {
        Camera = new ParkedCamera(new Vector2(TilesWide * World.TileSize / 2f, TilesHigh * World.TileSize / 2f));

        // Walkers start spread across the room above the lips. The lips lie on the row over the two floor rows.
        for (int index = 0; index < 1_000; index++)
        {
            Add(new Walker(
                new Vector2(
                    World.TileSize + ((index * 7) % ((TilesWide - 3) * World.TileSize)),
                    World.TileSize + ((index * 13) % ((TilesHigh - 7) * World.TileSize))),
                index,
                6f));
        }
    }
}
