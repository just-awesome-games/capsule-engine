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

    // How far the plate sinks under the player, in world units, and how fast, in units a second.
    private const float PressDepth = 2f;
    private const float PressSpeed = 16f;

    private readonly ColorRect _plate;
    private readonly Sequence _showLift = new();
    private Player? _watcher;

    /// <summary>The lift this plate starts. room.scene.json names it by its entry id.</summary>
    [Authorable]
    public required Lift Lift { get; set; }

    /// <param name="spawn">The plate's top-left corner.</param>
    public FloorSwitch(EntitySpawn spawn)
        : base(spawn)
    {
        _plate = Add(new ColorRect(Size) { Color = ColorRgba.FromHex("#a8433a") });

        BoxCollider2D plate = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        plate.ContactEntered += OnPlayerEntered;
        Add(plate);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context) => _showLift.Step();

    // A plate that leaves mid-show hands the player back control.
    /// <inheritdoc/>
    protected override void OnRemovedFromScene()
    {
        if (_showLift.IsRunning)
        {
            _showLift.Stop();
            _watcher!.HasControl = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnDebugPanel(DebugPanel panel) => panel.Field("Lift", Lift);

    private void OnPlayerEntered(ColliderContact2D contact)
    {
        if (Lift.Running || _showLift.IsRunning)
        {
            return;
        }

        _plate.Color = ColorRgba.FromHex("#5aa85a");
        _watcher = Scene.FindSingle<Player>();
        _showLift.Start(ShowLift(_watcher));
    }

    // The plate sinks under the player, who stands still while the camera visits the lift, watches it rise, and comes
    // back.
    private async Routine ShowLift(Player player)
    {
        player.HasControl = false;
        while (_plate.Offset.Y < PressDepth)
        {
            await Wait.Ticks(1);
            _plate.Offset = new Vector2(0f, MathF.Min(_plate.Offset.Y + (PressSpeed * Scene.StepSeconds), PressDepth));
        }

        Scene.Camera.Follow(Lift);
        await Wait.Ticks(PanTicks);
        Lift.Running = true;
        await Wait.Until(() => Lift.HasRisen);
        Scene.Camera.Follow(player);
        await Wait.Ticks(PanTicks);
        player.HasControl = true;
    }
}
