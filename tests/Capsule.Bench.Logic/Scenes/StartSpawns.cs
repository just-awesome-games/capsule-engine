using Capsule.Bench.Logic.Cameras;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 20 000 idle entities, every tenth of which adds one more as it starts: what a level of spawn points
/// costs to load under the <c>Startup</c> driver, and what an entity that does nothing costs a step.
/// </summary>
/// <remarks>
/// Row <c>StartChildren</c> adds each new entity as a child of the one starting, which lands it in the
/// middle of the tree.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = Children)]
public sealed class StartSpawns : Scene
{
    private const string Children = "StartChildren";

    public StartSpawns()
    {
        Camera = new ParkedCamera();

        bool children = Workloads.Running == Children;
        int count = Workloads.Scaled(20_000);
        for (int index = 0; index < count; index++)
        {
            Add<Entity>(index % 10 != 0 ? new Idle() : children ? new Parent() : new SpawnPoint());
        }
    }

    private sealed class Idle() : Entity(Vector2.Zero);

    private sealed class SpawnPoint() : Entity(Vector2.Zero)
    {
        protected override void OnStart() => Scene!.Add(new Idle());
    }

    private sealed class Parent() : Entity(Vector2.Zero)
    {
        protected override void OnStart() => new Idle().Parent = this;
    }
}
