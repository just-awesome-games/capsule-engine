namespace Capsule.Bench.Logic.Scenes;

/// <summary>1 000 walkers with a step height of 6 over the 4 and 6 unit lips of <c>steps-1k.scene.json</c>: grounded bodies stepping up onto and down off ledges.</summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Steps1k : WalkerRoomScene
{
    public Steps1k(SceneContent content)
        : base(content, stepHeight: 6f)
    {
    }
}
