using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>10 000 static sprite entities beside 100 wanderers: the static entities a step should not visit.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Props10k : Scene
{
    public Props10k()
    {
        Camera = new ParkedCamera();

        for (int index = 0; index < 10_000; index++)
        {
            Add(new Prop(new Vector2(4f + ((index % 125) * 5f), 4f + ((index / 125) * 4.4f))));
        }

        for (int index = 0; index < 100; index++)
        {
            Add(new Wanderer(new Vector2(6f + (index * 6.3f), (index * 37) % World.ViewportSize.Y), 20f + (index % 7)));
        }
    }
}
