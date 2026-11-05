using MinimalGame.Game.Entities;

namespace MinimalGame.Game.UI;

/// <summary>
/// The head-up display over the room, and the one place simulation state is bound to an interface
/// element: it decides where each element sits, finds the player for itself, and shows what the player
/// reports in the element. The elements know nothing of the game, and the display draws nothing of its
/// own.
/// </summary>
public sealed class PlayerHud : ScreenEntity
{
    private readonly Meter _health = new(Anchor.TopLeft, new Vector2(7f, 7f));

    public PlayerHud()
        : base(Anchor.Fill, Vector2.Zero) =>
        _health.Parent = this;

    // The player leaves with the scene as the display does. Nothing needs to unsubscribe.
    /// <inheritdoc/>
    protected override void OnStart()
    {
        Player player = Scene.FindSingle<Player>();
        ShowHealth(player);
        player.HealthChanged += () => ShowHealth(player);
    }

    private void ShowHealth(Player player) => _health.Fraction = (float)player.Health / player.Tuning.MaxHealth;
}
