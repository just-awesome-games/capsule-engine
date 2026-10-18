namespace Capsule.Bench.Logic.Scenes;

/// <summary>10 000 particles at a full pool, headless: the step alone, with nothing drawn.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Particles10kStep : ParticlesScene
{
    public Particles10kStep()
    {
    }
}
