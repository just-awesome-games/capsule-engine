using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>1 000 walkers on the walled tile ring of <c>crowd-1k.scene.json</c>: the entity pipeline, bodies and broadphase at that density.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Crowd1k : Scene
{
    // The room's extent in tiles, as the document authors it.
    private const int TilesWide = 16;

    private const int TilesHigh = 14;

    public Crowd1k(SceneContent content)
        : base(content)
    {
        Camera = new ParkedCamera(new Vector2(TilesWide * World.TileSize / 2f, TilesHigh * World.TileSize / 2f));

        for (int index = 0; index < 1_000; index++)
        {
            Add(new Walker(
                new Vector2(
                    World.TileSize + ((index * 7) % ((TilesWide - 2) * World.TileSize)),
                    World.TileSize + ((index * 13) % ((TilesHigh - 3) * World.TileSize))),
                index));
        }
    }
}
