namespace MinimalGame.Game.Entities;

/// <summary>
/// A lamp that sweeps a cone of light across the floor below it, warm while the cone is empty and red while it
/// holds the player. The light, the beam that shows it and the trigger that sees the player take one outline each
/// step.
/// </summary>
public sealed class Searchlight : Entity
{
    /// <summary>The steps one sweep takes, there and back.</summary>
    public const int SweepTicks = 240;

    // How far the cone reaches down, and how far its middle swings and its edges spread at most, across per
    // unit down. It narrows to a line at either end of its swing and spreads widest overhead.
    private const float Reach = 88f;
    private const float Swing = 0.6f;
    private const float Spread = 0.25f;

    // How far past the beam's end its light reaches: the floor tile's two-pixel top lip, which the light lands on.
    // The face below the lip stays dark, as ground the light cannot pass into.
    private const float Pool = 2f;

    private static readonly Vector2 HeadSize = new(8f, 4f);
    private static readonly ColorRgba HeadColor = new(40, 40, 44);

    /// <summary>The cone's colour while it sees nothing, lighting what it covers.</summary>
    public static readonly ColorRgba Warm = new(90, 70, 20);

    /// <summary>The cone's colour while it holds the player.</summary>
    public static readonly ColorRgba Seen = new(140, 20, 20);

    private readonly PolygonLight _light;
    private readonly ColorPolygon _beam;
    private readonly PolygonCollider2D _sight;
    private int _tick;

    /// <param name="spawn">Where the cone's tip hangs.</param>
    public Searchlight(EntitySpawn spawn)
        : base(spawn)
    {
        Add(new ColorRect(HeadSize) { Color = HeadColor, Offset = new Vector2(-HeadSize.X / 2f, -HeadSize.Y) });
        _light = Add(new PolygonLight { Color = Warm });

        // The beam glows on its own, at its own colour whatever the light around it.
        Entity beam = new(this, Vector2.Zero) { Unlit = true };
        _beam = beam.Add(new ColorPolygon { Color = Warm, Blend = BlendMode.Additive });
        _sight = Add(new PolygonCollider2D([Vector2.Zero, new Vector2(-1f, Reach), new Vector2(1f, Reach)])
        {
            Detects = new(CollisionLayers.Player),
            ReportsContacts = true,
        });
        _sight.ContactEntered += _ => Show(Seen);
        _sight.ContactExited += _ => Show(Warm);
        Shine();
    }

    protected override void OnStep(in StepContext context)
    {
        _tick = (_tick + 1) % SweepTicks;
        Shine();
    }

    // The middle swings evenly from one side to the other and back, and the cone is a line at either end, where
    // its trigger leaves the world until it spreads again.
    private void Shine()
    {
        float phase = (float)_tick / SweepTicks;
        float middle = Swing * (phase < 0.5f ? (4f * phase) - 1f : 3f - (4f * phase));
        float half = Spread * (1f - (MathF.Abs(middle) / Swing));
        ReadOnlySpan<Vector2> cone = [Vector2.Zero, new((middle - half) * Reach, Reach), new((middle + half) * Reach, Reach)];
        float reach = Reach + Pool;
        _light.SetPoints([Vector2.Zero, new((middle - half) * reach, reach), new((middle + half) * reach, reach)]);
        _beam.SetPoints(cone);
        _sight.SetPoints(cone);
    }

    private void Show(ColorRgba color)
    {
        _light.Color = color;
        _beam.Color = color;
    }
}
