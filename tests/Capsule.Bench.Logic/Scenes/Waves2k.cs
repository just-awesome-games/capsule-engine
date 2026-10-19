using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 2 000 stems that each shed and regrow a contact-reporting, screen-notified child on the same step, every 15 steps:
/// 2 000 removals and 2 000 child joins landing in one drain. A wave is one step in 15, and the p95 step is a wave step.
/// </summary>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Paced)]
public sealed class Waves2k : Scene
{
    private const int Period = 15;

    public Waves2k()
    {
        Camera = new ParkedCamera();

        for (int index = 0; index < 2_000; index++)
        {
            Add(new Stem(new Vector2(6f + ((index % 50) * 12.8f), 12f + ((index / 50) * 9f)), Period));
        }
    }
}
