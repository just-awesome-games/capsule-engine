namespace MinimalGame.Game.Entities;

/// <summary>
/// A plate on the floor that starts the lift its placement names when the player steps on it, and shows
/// the player the lift rising.
/// </summary>
public sealed class FloorSwitch : Entity
{
    private static readonly Vector2 Size = new(16f, 4f);

    // Steps the camera takes to ease between the player and the lift.
    private const int PanTicks = 45;

    private readonly ColorRect _plate;
    private readonly Sequence _showLift = new();

    /// <summary>The lift this plate starts. room.scene.json names it by its entry id.</summary>
    [Authorable]
    public required Lift Lift { get; set; }

    /// <param name="spawn">The plate's top-left corner.</param>
    public FloorSwitch(EntitySpawn spawn)
        : base(spawn)
    {
        _plate = new ColorRect(Size) { Color = ColorRgba.FromHex("#a8433a") };
        Add(_plate);

        BoxCollider2D plate = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        plate.ContactEntered += OnPlayerEntered;
        Add(plate);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context) => _showLift.Step();

    /// <inheritdoc/>
    protected override void OnDebugPanel(DebugPanel panel) => panel.Field("Lift", Lift);

    private void OnPlayerEntered(ColliderContact2D contact)
    {
        if (Lift.Running || _showLift.IsRunning)
        {
            return;
        }

        _plate.Color = ColorRgba.FromHex("#5aa85a");
        _showLift.Start(ShowLift(Scene.FindSingle<Player>()));
    }

    // The player stands still while the camera visits the lift, watches it rise, and comes back.
    private IEnumerable<Wait> ShowLift(Player player)
    {
        player.HasControl = false;
        Scene.Camera.Follow(Lift);
        yield return Wait.Ticks(PanTicks);
        Lift.Running = true;
        yield return Wait.Until(() => Lift.HasRisen);
        Scene.Camera.Follow(player);
        yield return Wait.Ticks(PanTicks);
        player.HasControl = true;
    }
}
