namespace MinimalGame.Game.Entities;

/// <summary>
/// A one-way slab that circles its spawn point at a constant rate, which the player jumps up through
/// and drops down through.
/// </summary>
public sealed class CirclingPlatform : Entity
{
    private static readonly Vector2 Size = new(24f, 6f);
    private const float Radius = 24f;
    private const int TurnTicks = 240;

    private readonly Vector2 _centre;

    // Steps into the turn, which give the angle.
    private int _tick;

    public CirclingPlatform(EntitySpawn spawn)
        : base(spawn)
    {
        _centre = Position;
        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Platform, OneWay = true });
        Add(new ColorRect(Size) { Color = ColorRgba.FromHex("#88627a") });
        Teleport(Place(_tick));
    }

    protected override void OnStep(in StepContext context)
    {
        _tick = (_tick + 1) % TurnTicks;
        Position = Place(_tick);
    }

    // The slab's top-left corner, with its centre on the circle.
    private Vector2 Place(int tick)
    {
        float angle = MathF.Tau * tick / TurnTicks;

        return _centre + (Radius * new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle))) - (Size / 2f);
    }
}
