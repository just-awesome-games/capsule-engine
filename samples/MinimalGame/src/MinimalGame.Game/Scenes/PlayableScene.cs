using System.Numerics;
using Capsule;
using Capsule.Rendering;
using Capsule.Scenes;
using MinimalGame.Game.Entities;
using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// What every playable scene is made of, with the level left to the document: the head-up display,
/// the pause menu, and returning to the <see cref="MainMenu"/> at no health. A level is a document
/// that names it as its <c>baseScene</c>. The camera comes from the document, and the display and the
/// menu are installed in the constructor so their contents are collected for its preload.
/// </summary>
public abstract class PlayableScene : Scene
{
    // The centre texel is the hotspot, so a bolt flies where the crosshair points.
    private static readonly Sprite Crosshair = new(CapsuleAssets.Textures.CrosshairTexture, new TextureRegion(0, 0, 9, 9), new Vector2(4f, 4f));

    private readonly PauseMenu _pauseMenu = new();

    /// <summary>The room's spark pool, as far up as sparks reach and no further.</summary>
    public EntityPool<SparkBurst> Sparks { get; } = new(() => new SparkBurst(), capacity: 8);

    /// <summary>The body the document placed, for the level that wants to reach it.</summary>
    protected Player Player { get; private set; } = null!;

    protected PlayableScene(SceneContent content)
        : base(content)
    {
        Add(new PlayerHud());
        Add(_pauseMenu);
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        Player = FindSingle<Player>();

        // Crossfades from whatever was playing, or fades in alone under --scene scenes/room.
        Run.Game.Music.Play(CapsuleAssets.Audio.Music.RoomSound);
    }

    // The scene steps through its own pause and owns the key that opens and closes it. Losing window
    // focus opens it too, and regaining window focus leaves it open.
    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WindowFocusLost && !Paused)
        {
            _pauseMenu.Open();
        }
        else if (context.Input.WasPressed(GameInput.Pause))
        {
            if (Paused)
            {
                _pauseMenu.Close();
            }
            else
            {
                _pauseMenu.Open();
            }
        }

        // The pause menu is pointed at with the system arrow. Set every step because the next room
        // starts before this one stops, and clearing it on stop would take the next room's crosshair.
        Run.Cursor.Image = Paused ? null : Crosshair;
    }

    // Health is spent by a contact handler, so the death test runs where contacts have settled: the
    // step that lands the killing damage is the step that leaves the room.
    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context)
    {
        if (Player.Health == 0)
        {
            Run.Game.Music.Stop(seconds: 0.5f);
            Run.RequestScene<MainMenu>();
        }
    }
}
