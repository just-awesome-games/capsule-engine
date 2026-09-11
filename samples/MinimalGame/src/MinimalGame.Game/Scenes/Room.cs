using System.Numerics;
using Capsule;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;
using MinimalGame.Game.Cameras;
using MinimalGame.Game.Entities;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The playable room: a scene that is a document and a class at once. <c>[SceneDocument("room")]</c>
/// names <c>Assets/Scenes/room.scene.json</c> and the <see cref="SceneContent"/> constructor is the
/// claim — a scene with one is composed from its document, entry by entry in file order. This class is
/// the code half: which camera it installs, the head-up display it adds over the document's contents,
/// and what quitting means. <c>halls/hall.scene.json</c> is the contrasting case — a document claimed by
/// no class at all, which still loads and plays as a plain <see cref="Scene"/>.
/// </summary>
[SceneDocument("room")]
public sealed class Room : Scene
{
    private Player _player = null!;

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
        Add(new HealthBar(_player));
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }

    // Health is spent by a contact handler, so the death test runs where contacts have settled: the
    // step that lands the killing damage is the step that leaves the room.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        if (_player.Health == 0)
        {
            RequestScene<MainMenu>();
        }
    }

    /// <summary>
    /// Two flat rects anchored to the canvas's top-left corner: a dark bed, and a fill as wide a share
    /// of it as the player has health left. Nothing here is a widget — the fill rewrites its own
    /// <see cref="ColorRect.Size"/> each late step, where the contact that spent a point has settled.
    /// </summary>
    private sealed class HealthBar : ScreenEntity
    {
        // Canvas pixels in from the corner on both axes.
        private static readonly Vector2 Margin = new(8f, 8f);

        // The bed's extent in canvas pixels, and the fill's at full health.
        private static readonly Vector2 Span = new(64f, 6f);

        private static readonly ColorRgba BedColor = new(24, 24, 32);
        private static readonly ColorRgba FillColor = new(222, 96, 96);

        private readonly Player _player;
        private readonly ColorRect _fill = new(Span) { Color = FillColor, ZIndex = 1 };

        internal HealthBar(Player player)
            : base(Anchor.TopLeft, Margin)
        {
            _player = player;

            // Both rects are on one entity, so the renderer's own band is what layers them.
            Add(new ColorRect(Span) { Color = BedColor });
            Add(_fill);
        }

        /// <inheritdoc/>
        protected override void OnLateStep(in StepContext context) =>
            _fill.Size = new Vector2(Span.X * _player.Health / _player.Tuning.MaxHealth, Span.Y);
    }
}
