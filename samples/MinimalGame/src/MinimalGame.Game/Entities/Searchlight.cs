namespace MinimalGame.Game.Entities;

/// <summary>
/// A lamp that sweeps a cone of light across the floor below it, warm while the cone is empty and red while it
/// holds the player. The light and the trigger that sees the player take one outline each step.
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

    private static readonly Vector2 HeadSize = new(8f, 4f);
    private static readonly ColorRgba HeadColor = new(40, 40, 44);

    /// <summary>The cone's colour while it sees nothing, added over what it covers.</summary>
    public static readonly ColorRgba Warm = new(90, 70, 20);

    /// <summary>The cone's colour while it holds the player.</summary>
    public static readonly ColorRgba Seen = new(140, 20, 20);

    private readonly ColorPolygon _light;
    private readonly PolygonCollider2D _sight;
    private int _tick;

    /// <param name="spawn">Where the cone's tip hangs.</param>
    public Searchlight(EntitySpawn spawn)
        : base(spawn)
    {
        Add(new ColorRect(HeadSize) { Color = HeadColor, Offset = new Vector2(-HeadSize.X / 2f, -HeadSize.Y) });
        _light = Add(new ColorPolygon { Color = Warm, Blend = BlendMode.Additive });
        _sight = Add(new PolygonCollider2D([Vector2.Zero, new Vector2(-1f, Reach), new Vector2(1f, Reach)])
        {
            Detects = new(CollisionLayers.Player),
            ReportsContacts = true,
        });
        _sight.ContactEntered += _ => _light.Color = Seen;
        _sight.ContactExited += _ => _light.Color = Warm;
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
        _light.SetPoints(cone);
        _sight.SetPoints(cone);
    }
}
