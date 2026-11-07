using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 200 visible sprite renderers plus 100 000 static ones far outside the view, across 32 scrolled depth
/// bands: what a level pays per renderer for what it holds but does not show.
/// </summary>
/// <remarks>
/// Row <c>LargeWorld10k</c> holds 10 000 offscreen, for the slope. Row <c>LargeWorldChurn</c> also takes a
/// pooled renderer into a middle band and returns the last one every step, a spawn and a despawn.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = TenThousand)]
[Workload(WorkloadKind.Simulation, Row = Churn)]
public sealed class LargeWorld : Scene
{
    private const string TenThousand = "LargeWorld10k";
    private const string Churn = "LargeWorldChurn";
    private const int Visible = 200;
    private const int Bands = 32;

    private readonly EntityPool<Placed>? _spawns;
    private Placed? _spawned;

    public LargeWorld()
    {
        Vector2 centre = World.ViewportSize / 2f;
        Camera = new ParkedCamera(centre);
        int count = Visible + (Workloads.Running == TenThousand ? 10_000 : 100_000);

        for (int index = 0; index < count; index++)
        {
            Vector2 position = index < Visible
                ? centre + new Vector2((index * 37 % 200) - 100, (index * 53 % 120) - 60)
                : new Vector2(20_000f + (index % 1_000 * 200f), 20_000f + (index / 1_000 * 200f));
            Add(new Placed(position, index % Bands));
        }

        if (Workloads.Running == Churn)
        {
            _spawns = new EntityPool<Placed>(() => new Placed(centre, Bands / 2), capacity: 2);
        }
    }

    protected override void OnStep(in StepContext context)
    {
        if (_spawns is null)
        {
            return;
        }

        if (_spawned is not null)
        {
            Remove(_spawned);
        }

        _spawned = _spawns.Take();
        Add(_spawned);
    }

    private sealed class Placed : Entity
    {
        internal Placed(Vector2 position, int band)
            : base(position)
        {
            ZIndex = band;
            ScrollFactor = new Vector2(0.4f + (band * 0.03f));
            Add(new SpriteRenderer(SpriteField.Tile));
        }
    }
}
