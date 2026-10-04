using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>1 000 drifting sprites whose frame box turns on and off every few steps, half of them mirrored by scale: box placement and the collision world's churn.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class FrameBoxes1k : Scene
{
    public FrameBoxes1k()
    {
        Camera = new ParkedCamera();

        Vector2 extent = World.ViewportSize - new Vector2(48f, 24f);
        for (int index = 0; index < 1_000; index++)
        {
            Add(new Striker(new Vector2(24f + ((index * 7) % extent.X), 24f + ((index * 13) % extent.Y)), index));
        }
    }
}
