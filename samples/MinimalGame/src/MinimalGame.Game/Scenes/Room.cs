using System.Numerics;
using Capsule;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;
using MinimalGame.Game.Cameras;
using MinimalGame.Game.Entities;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The playable room: a scene that is a document and a class at once. The document is
/// <c>Assets/Scenes/room.scene.json</c>, which the build validates and re-emits to
/// <c>assets/scenes/room.scene.json</c> beside the executable; <c>[SceneDocument("room")]</c> names
/// it, and without the attribute the key this class's namespace names would be the same: it sits
/// directly under <c>MinimalGame.Game.Scenes</c>, so the <c>Scenes</c> segment falls away and
/// <c>Room</c> claims <c>room</c>. The
/// <see cref="SceneContent"/> constructor is the claim — a scene with one is composed from its
/// document, entry by entry in file order: the tile map first, then the <c>player</c> and
/// <c>sensor</c> placements.
/// <para>
/// This class is the code half of that scene: which camera it installs, the head-up display it adds
/// over the document's contents, and what quitting means. The camera's own framing lives in
/// <see cref="GameCamera"/>, not here.
/// <c>halls/hall.scene.json</c> is the contrasting case — a document claimed by no class at all, which
/// still loads and plays as a plain <see cref="Scene"/>.
/// </para>
/// </summary>
[SceneDocument("room")]
public sealed class Room : Scene
{
    private Player _player = null!;
    private HealthBar _health = null!;

    public Room(SceneContent content)
        : base(content)
    {
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        Camera = new GameCamera();

        // The document places the player; the interface over it is the scene's own.
        _player = FindSingle<Player>();
        _health = new HealthBar(_player);
        Add(_health);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }

    // Health is spent by a contact handler, so both the bar and the death test run where contacts have
    // settled: the step that lands the damage is the step that shows it.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        _health.Refresh();

        if (_player.Health == 0)
        {
            RequestScene<MainMenu>();
        }
    }

    /// <summary>
    /// Two flat rects on the screen layer, anchored to the canvas's top-left corner so they hold that
    /// corner whatever the window is: a dark bed, and a fill as wide a share of it as the player has
    /// health left. Nothing here is a widget — the bar is a rect whose <see cref="ColorRect.Size"/> the
    /// scene's late step writes.
    /// </summary>
    private sealed class HealthBar : Entity
    {
        /// <summary>Canvas pixels in from the corner on both axes.</summary>
        private static readonly Vector2 Margin = new(8f, 8f);

        /// <summary>The bed's extent in canvas pixels, and the fill's at full health.</summary>
        private static readonly Vector2 Span = new(64f, 6f);

        private static readonly ColorRgba BedColor = new(24, 24, 32);
        private static readonly ColorRgba FillColor = new(222, 96, 96);

        private readonly Player _player;
        private readonly ColorRect _fill = new(Span) { Color = FillColor, ZIndex = 1 };

        internal HealthBar(Player player)
            : base(Margin)
        {
            _player = player;

            Space = RenderSpace.Screen;

            // Both rects are on one entity, so the renderer's own band is what layers them.
            Add(new ColorRect(Span) { Color = BedColor });
            Add(_fill);
        }

        internal void Refresh() =>
            _fill.Size = new Vector2(Span.X * _player.Health / Player.MaxHealth, Span.Y);
    }
}
