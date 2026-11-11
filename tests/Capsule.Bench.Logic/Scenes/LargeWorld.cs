using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 200 visible sprite renderers plus 100 000 static ones far outside the view, across 32 scrolled depth
/// bands: what a level pays per renderer for what it holds but does not show.
/// </summary>
/// <remarks>
/// Row <c>LargeWorld10k</c> holds 10 000 offscreen, for the slope. Row <c>LargeWorldChurn</c> also takes a
/// pooled renderer into a middle band and returns the last one every step, a spawn and a despawn. Row
/// <c>LargeWorldWave</c> takes 200 into the lowest band and returns the last 200. Row
/// <c>LargeWorldRebanded</c> moves one visible renderer to another band every step. Row
/// <c>LargeWorldAttached</c> takes one as a child of the first root and returns the last one, a join in
/// the middle of the tree.
/// </remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = TenThousand)]
[Workload(WorkloadKind.Simulation, Row = Churn)]
[Workload(WorkloadKind.Simulation, Row = Wave)]
[Workload(WorkloadKind.Simulation, Row = Rebanded)]
[Workload(WorkloadKind.Simulation, Row = Attached)]
public sealed class LargeWorld : Scene
{
    private const string TenThousand = "LargeWorld10k";
    private const string Churn = "LargeWorldChurn";
    private const string Wave = "LargeWorldWave";
    private const string Rebanded = "LargeWorldRebanded";
    private const string Attached = "LargeWorldAttached";
    private const int Visible = 200;
    private const int Bands = 32;

    private readonly EntityPool<Placed>? _spawns;
    private readonly List<Placed> _spawned = [];
    private readonly int _perStep;
    private readonly Placed? _rebanded;
    private readonly Placed? _host;

    public LargeWorld()
    {
        Vector2 centre = World.ViewportSize / 2f;
        Camera = new ParkedCamera(centre);
        int count = Visible + Workloads.Scaled(Workloads.Running == TenThousand ? 10_000 : 100_000);

        for (int index = 0; index < count; index++)
        {
            Vector2 position = index < Visible
                ? centre + new Vector2((index * 37 % 200) - 100, (index * 53 % 120) - 60)
                : new Vector2(20_000f + (index % 1_000 * 200f), 20_000f + (index / 1_000 * 200f));
            Placed placed = new(position, index % Bands);
            Add(placed);
            _rebanded ??= Workloads.Running == Rebanded ? placed : null;
            _host ??= Workloads.Running == Attached ? placed : null;
        }

        _perStep = Workloads.Running switch
        {
            Churn or Attached => 1,
            Wave => 200,
            _ => 0,
        };
        if (_perStep > 0)
        {
            int band = Workloads.Running == Wave ? 0 : Bands / 2;
            bool scrolls = _host is null;
            _spawns = new EntityPool<Placed>(() => new Placed(centre, band, scrolls), capacity: 2 * _perStep);
        }
    }

    protected override void OnStep(in StepContext context)
    {
        if (_rebanded is not null)
        {
            _rebanded.ZIndex = (int)(context.Tick % 2);
        }

        if (_spawns is null)
        {
            return;
        }

        foreach (Placed spawned in _spawned)
        {
            Remove(spawned);
        }

        _spawned.Clear();
        for (int index = 0; index < _perStep; index++)
        {
            Placed spawned = _spawns.Take();
            if (_host is null)
            {
                Add(spawned);
            }
            else
            {
                spawned.Parent = _host;
            }

            _spawned.Add(spawned);
        }
    }

    private sealed class Placed : Entity
    {
        internal Placed(Vector2 position, int band, bool scrolls = true)
            : base(position)
        {
            ZIndex = band;
            if (scrolls)
            {
                ScrollFactor = new Vector2(0.4f + (band * 0.03f));
            }

            Add(new SpriteRenderer(SpriteField.Tile));
        }
    }
}
