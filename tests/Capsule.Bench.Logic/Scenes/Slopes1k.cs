using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>1 000 walkers over the 1:1, 1:2 and 1:4 hills of <c>slopes-1k.scene.json</c>: grounded bodies walking up, over and down slopes.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Slopes1k : Scene
{
    // The room's extent in tiles, as the document authors it.
    private const int TilesWide = 34;

    private const int TilesHigh = 14;

    public Slopes1k(SceneContent content)
        : base(content)
    {
        Camera = new ParkedCamera(new Vector2(TilesWide * World.TileSize / 2f, TilesHigh * World.TileSize / 2f));

        // Walkers start spread across the room above the hills. The hills lie on the row over the two floor rows.
        for (int index = 0; index < 1_000; index++)
        {
            Add(new Walker(
                new Vector2(
                    World.TileSize + ((index * 7) % ((TilesWide - 3) * World.TileSize)),
                    World.TileSize + ((index * 13) % ((TilesHigh - 7) * World.TileSize))),
                index));
        }
    }
}
