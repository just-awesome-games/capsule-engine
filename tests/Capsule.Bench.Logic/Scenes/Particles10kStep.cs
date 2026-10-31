namespace Capsule.Bench.Logic.Scenes;

/// <summary>10 000 particles at a full pool, headless: the step alone, with nothing drawn.</summary>
/// <remarks>Row <c>Particles10kNoise</c> swirls every particle through a noise field drifting across the view.</remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = Noise)]
public sealed class Particles10kStep : ParticlesScene
{
    private const string Noise = "Particles10kNoise";

    public Particles10kStep()
        : base(Workloads.Running == Noise ? new ParticleNoise(strength: 15f, size: 48f) { Scroll = new Vector2(10f, 0f) } : default)
    {
    }
}
