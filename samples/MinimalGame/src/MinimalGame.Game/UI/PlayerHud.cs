using System.Numerics;
using Capsule;
using Capsule.Scenes;
using Capsule.UI;
using MinimalGame.Game.Entities;

namespace MinimalGame.Game.UI;

/// <summary>
/// The head-up display over the room, and the one place simulation state is bound to an interface
/// element: it decides where each element sits, finds the player for itself, and writes what it reads
/// into the element. The elements know nothing of the game, and the display draws nothing of its own.
/// </summary>
public sealed class PlayerHud : ScreenEntity
{
    private readonly HealthBar _healthBar = new(Anchor.TopLeft, new Vector2(7f, 7f));

    private Player _player = null!;

    public PlayerHud()
        : base(Anchor.Fill, Vector2.Zero) =>
        _healthBar.Parent = this;

    /// <inheritdoc/>
    protected override void OnStart() => _player = Scene.FindSingle<Player>();

    // Health is spent by a contact handler, so the read runs where contacts have settled.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context) =>
        _healthBar.Fraction = (float)_player.Health / _player.Tuning.MaxHealth;
}
