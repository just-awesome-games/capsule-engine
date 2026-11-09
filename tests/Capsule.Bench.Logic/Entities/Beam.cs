namespace Capsule.Bench.Logic.Entities;

/// <summary>
/// A cone of light that sweeps back and forth and narrows to a line at each end of its swing, its additive fill
/// and its sensor fed one outline every step, as a searchlight is. It counts the actors it sees in <see cref="Seen"/>.
/// </summary>
public sealed class Beam : Entity
{
    private const int SweepTicks = 120;
    private const float Reach = 48f;
    private const float Swing = 0.6f;
    private const float Spread = 0.25f;

    private readonly ColorPolygon _fill;
    private readonly PolygonCollider2D _sight;
    private int _tick;

    public int Seen { get; private set; }

    public Beam(Vector2 position, int phase)
        : base(position)
    {
        _tick = phase % SweepTicks;
        _fill = Add(new ColorPolygon { Color = new ColorRgba(40, 30, 10), Blend = BlendMode.Additive });
        _sight = Add(new PolygonCollider2D([Vector2.Zero, new Vector2(-1f, Reach), new Vector2(1f, Reach)])
        {
            Detects = new(CollisionLayers.Actor),
            ReportsContacts = true,
        });
        _sight.ContactEntered += _ => Seen++;
        Shine();
    }

    protected override void OnStep(in StepContext context)
    {
        _tick = (_tick + 1) % SweepTicks;
        Shine();
    }

    private void Shine()
    {
        float phase = (float)_tick / SweepTicks;
        float middle = Swing * (phase < 0.5f ? (4f * phase) - 1f : 3f - (4f * phase));
        float half = Spread * (1f - (MathF.Abs(middle) / Swing));
        ReadOnlySpan<Vector2> cone = [Vector2.Zero, new((middle - half) * Reach, Reach), new((middle + half) * Reach, Reach)];
        _fill.SetPoints(cone);
        _sight.SetPoints(cone);
    }
}
