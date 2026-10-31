namespace MinimalGame.Game.Entities;

/// <summary>
/// The water pouring over a room's falls tiles, which bears the player down at its own gravity while the
/// player is inside it. Drops fall through it and splash into spray where they land.
/// </summary>
public sealed class Waterfall : Entity
{
    private static readonly ColorRgba Spray = ColorRgba.FromHex("#d8f0ffc0");

    /// <summary>The area's width and height in world units.</summary>
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    /// <summary>The gravity the player falls by inside, in world units per second squared.</summary>
    [Authorable(Required = true)]
    public float Gravity { get; private set; }

    /// <param name="spawn">The area's top-left corner.</param>
    public Waterfall(EntitySpawn spawn)
        : base(spawn)
    {
        BoxCollider2D area = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        area.ContactEntered += OnPlayerEntered;
        area.ContactExited += OnPlayerExited;
        Add(area);

        // Spray thrown up where a drop lands, wandering as it fades.
        ParticleEmitter spray = new(Sprite.White, capacity: 96)
        {
            Direction = -Vector2.UnitY,
            Spread = 150f,
            Speed = (30f, 60f),
            Damping = 2.5f,
            Noise = new ParticleNoise(strength: 20f, size: 20f),
            Lifetime = (0.5f, 0.9f),
            Scale = (1.5f, 2.5f),
            Color = Glow.FadeToClear(Spray),
        };

        // Drops off the lip that die on whatever they land on, each into three of the spray.
        Add(new ParticleEmitter(Sprite.White, capacity: 32)
        {
            Offset = new Vector2(Size.X / 2f, 0f),
            Shape = EmitShape.Rect(Size.X, 0f),
            Direction = Vector2.UnitY,
            Speed = (60f, 90f),
            Gravity = new Vector2(0f, 400f),
            Lifetime = (2f, 2f),
            Rate = 30f,
            Color = Spray,
            Collision = ParticleCollision.Kill(CollisionLayers.Blocking),
            EmitOnDeath = new SubEmission(spray, count: 3),
        });
        Add(spray);
    }

    private void OnPlayerEntered(ColliderContact2D contact) => ((Player)contact.OtherEntity!).GravityOverride = Gravity;

    private void OnPlayerExited(ColliderContact2D contact) => ((Player)contact.OtherEntity!).GravityOverride = null;
}
