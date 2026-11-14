namespace MinimalGame.Game.Entities;

/// <summary>A low block that travels its placement's path at a steady speed.</summary>
public sealed class Shuttle : Entity
{
    private const float TravelSpeed = 48f;
    private static readonly Vector2 Size = new(24f, 8f);

    /// <summary>
    /// The points the block travels, relative to its placement. A path that ends where it starts loops,
    /// and any other runs to its end and back.
    /// </summary>
    [Authorable]
    public Path2D Path { get; private set; } = new(Vector2.Zero, new Vector2(48f, 0f));

    public Shuttle(EntitySpawn spawn)
        : base(spawn)
    {
        Add(new BoxCollider2D(Size) { Layer = CollisionLayers.Platform });
        Add(new ColorRect(Size) { Color = ColorRgba.FromHex("#5a8870") });
        Add(new PathFollower2D(Path) { Speed = TravelSpeed, Loop = Path.Closed ? LoopMode.Repeat : LoopMode.PingPong });
    }
}
