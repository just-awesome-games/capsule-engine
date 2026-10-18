using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary><see cref="Crowd1k"/>'s 1 000 walkers dropped on one spot of the same room, walking as two packs that cross and turn together: the entity pipeline, bodies and broadphase with every body inside every other's box.</summary>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
public sealed class Crowd1kStacked : Scene
{
    // The room's extent in tiles, as the document authors it.
    private const int TilesWide = 16;

    private const int TilesHigh = 14;

    public Crowd1kStacked(SceneContent content)
        : base(content)
    {
        Vector2 centre = new(TilesWide * World.TileSize / 2f, TilesHigh * World.TileSize / 2f);
        Camera = new ParkedCamera(centre);

        for (int index = 0; index < 1_000; index++)
        {
            Add(new Walker(centre, index));
        }
    }
}
