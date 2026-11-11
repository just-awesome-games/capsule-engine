using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>3 000 wandering roots in one band with <see cref="Scene.YSort"/> on: the per-frame Y refresh and re-sort of the draw order.</summary>
/// <remarks>Row <c>YSortChurn</c> also takes 50 pooled roots and returns the last 50 every step, the shots of a top-down game.</remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = Churn)]
[Workload(WorkloadKind.Rendering)]
public sealed class YSort : Scene
{
    private const string Churn = "YSortChurn";
    private const int PerStep = 50;

    private static readonly int Count = Workloads.Scaled(3_000);

    private readonly EntityPool<Wanderer>? _spawns;
    private readonly List<Wanderer> _spawned = [];

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

        if (Workloads.Running == Churn)
        {
            _spawns = new EntityPool<Wanderer>(() => new Wanderer(extent / 2f, 30f), capacity: 2 * PerStep);
        }
    }

    protected override void OnStep(in StepContext context)
    {
        if (_spawns is null)
        {
            return;
        }

        foreach (Wanderer spawned in _spawned)
        {
            Remove(spawned);
        }

        _spawned.Clear();
        for (int index = 0; index < PerStep; index++)
        {
            Wanderer spawned = _spawns.Take();
            spawned.Position = new Vector2(index * 12f, (context.Tick * 7 % 300) + index);
            Add(spawned);
            _spawned.Add(spawned);
        }
    }
}
