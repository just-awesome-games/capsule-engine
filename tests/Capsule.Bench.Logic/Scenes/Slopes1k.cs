namespace Capsule.Bench.Logic.Scenes;

/// <summary>1 000 walkers over the 1:1, 1:2 and 1:4 hills of <c>slopes-1k.scene.json</c>: grounded bodies walking up, over and down slopes.</summary>
/// <remarks>
/// Row <c>CenterRest1k</c> gives every walker <see cref="Capsule.Physics.KinematicBody2D.RestsOnCenter"/>:
/// grounded bodies standing on the floor under their centers.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = CenterRest)]
public sealed class Slopes1k : WalkerRoomScene
{
    private const string CenterRest = "CenterRest1k";

    public Slopes1k(SceneContent content)
        : base(content, restsOnCenter: Workloads.Running == CenterRest)
    {
    }
}
