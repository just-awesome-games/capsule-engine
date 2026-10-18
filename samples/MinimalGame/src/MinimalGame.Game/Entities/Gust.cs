namespace MinimalGame.Game.Entities;

/// <summary>
/// An area of wind that pushes the player at its velocity while the player is inside it, throughout or in timed
/// bursts, drawn as streaks blowing its way.
/// </summary>
public sealed class Gust : Entity
{
    private readonly ParticleEmitter _streaks;

    // Steps into the loop, whether it blows now, and the player inside.
    private int _cycleTick;
    private bool _blowing = true;
    private Player? _player;

    /// <summary>The area's width and height in world units.</summary>
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    /// <summary>The push, in world units per second.</summary>
    [Authorable(Required = true)]
    public Vector2 Velocity { get; private set; }

    /// <summary>Steps it blows in each loop, which runs from the scene's first step.</summary>
    [Authorable]
    public int BlowTicks { get; private set; }

    /// <summary>Steps it falls calm after blowing in each loop. At 0 it blows throughout.</summary>
    [Authorable]
    public int CalmTicks { get; private set; }

    /// <param name="spawn">The area's top-left corner.</param>
    public Gust(EntitySpawn spawn)
        : base(spawn)
    {
        BoxCollider2D area = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        area.ContactEntered += OnPlayerEntered;
        area.ContactExited += OnPlayerExited;
        Add(area);

        // Streaks born across the whole area outrun the wind they show.
        float streak = Velocity.Length() * 2f;
        _streaks = new ParticleEmitter(Sprite.White, capacity: 24)
        {
            Offset = Size / 2f,
            Shape = EmitShape.Rect(Size.X, Size.Y),
            Direction = Velocity,
            Speed = (streak, streak),
            Lifetime = (0.2f, 0.4f),
            Rate = 40f,
            PrewarmSeconds = 0.4f,
            Color = ColorRgba.FromHex("#ffffff78"),
        };
        Add(_streaks);
    }

    // Decided for the next step. The push and the streaks change on the same step whichever steps first,
    // the player or the gust. The streaks already out blow on as the push stops.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        if (CalmTicks > 0)
        {
            _cycleTick = (_cycleTick + 1) % (BlowTicks + CalmTicks);
            _blowing = _cycleTick < BlowTicks;
            _streaks.Emitting = _blowing;
        }

        _player?.Wind = _blowing ? Velocity : Vector2.Zero;
    }

    private void OnPlayerEntered(ColliderContact2D contact) => _player = (Player)contact.OtherEntity!;

    private void OnPlayerExited(ColliderContact2D contact)
    {
        _player!.Wind = Vector2.Zero;
        _player = null;
    }
}
