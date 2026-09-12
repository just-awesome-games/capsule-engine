using System.Numerics;
using Capsule;
using Capsule.Scenes;
using MinimalGame.Game.Entities;

namespace MinimalGame.Game.UI;

/// <summary>
/// The head-up display over the room, and the one place simulation state is bound to an interface
/// element: it decides where each element sits, finds the player for itself, and writes what it reads
/// into the element. The elements know nothing of the game.
/// <para>
/// A <see cref="ScreenEntity"/> drawing nothing of its own, because the interface it holds belongs on
/// the screen layer whether or not the display has a look.
/// </para>
/// </summary>
public sealed class PlayerHud : ScreenEntity
{
    private readonly HealthBar _healthBar = new(Anchor.TopLeft, new Vector2(8f, 8f));

    private Player _player = null!;

    public PlayerHud()
        : base(Anchor.TopLeft, Vector2.Zero)
    {
    }

    // The bar is anchored to the canvas rather than to this entity, so it is the scene's peer: an
    // entity adds another by reaching the scene it has just joined.
    /// <inheritdoc/>
    protected override void OnAddedToScene() => Scene!.Add(_healthBar);

    /// <inheritdoc/>
    protected override void OnStart() => _player = Scene!.FindSingle<Player>();

    // Health is spent by a contact handler, so the read runs where contacts have settled.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context) =>
        _healthBar.Fraction = (float)_player.Health / _player.Tuning.MaxHealth;
}
