using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>About 2 000 pooled bullets a second fired across the pillared room of <c>bullets-2k.scene.json</c>, each reporting contacts until a wall ends it: collider registration churn and per-collider contact tracking.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Bullets2k : Scene
{
    // The room's extent in tiles, as the document authors it.
    private const int TilesWide = 40;

    private const int TilesHigh = 23;

    public Bullets2k(SceneContent content)
        : base(content)
    {
        Vector2 centre = new(TilesWide * World.TileSize / 2f, TilesHigh * World.TileSize / 2f);
        Camera = new ParkedCamera(centre);
        Add(new Gun(centre));
    }
}
