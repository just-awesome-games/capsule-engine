namespace Capsule.Bench.Logic.Scenes;

/// <summary>10 000 particles at a full pool, drawn: spawn, age, evaluate and submit at that density.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class Particles10k : ParticlesScene
{
    public Particles10k()
    {
    }
}
