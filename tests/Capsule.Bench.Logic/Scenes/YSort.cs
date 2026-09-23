using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>3 000 wandering roots in one band with <see cref="Scene.YSort"/> on: the per-frame Y refresh and re-sort of the draw order.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class YSort : Scene
{
    private const int Count = 3_000;

    public YSort()
    {
        Camera = new ParkedCamera();
        base.YSort = true;

        Vector2 extent = World.ViewportSize;
        for (int index = 0; index < Count; index++)
        {
            float x = (index * 7) % extent.X;
            float y = (index * 13) % extent.Y;
            float speed = 10f + ((index * 37) % 50);
            Add(new Wanderer(new Vector2(x, y), index % 2 == 0 ? speed : -speed));
        }
    }
}
