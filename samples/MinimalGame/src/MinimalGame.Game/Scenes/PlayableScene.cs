using System.Numerics;
using Capsule;
using Capsule.Audio;
using Capsule.Rendering;
using Capsule.Scenes;
using MinimalGame.Game.Entities;
using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The base of every level document (<c>"baseScene": "playable-scene"</c>): the head-up display, the
/// pause menu, arriving at the <see cref="Entrance"/> an <see cref="Arrival"/> names, and returning to
/// the <see cref="MainMenu"/> at no health. The document supplies the level, the camera and the music.
/// </summary>
public abstract class PlayableScene : Scene
{
    // The centre texel is the hotspot, so a bolt flies where the crosshair points.
    private static readonly Sprite Crosshair = new(CapsuleAssets.Textures.CrosshairTexture, new TextureRegion(0, 0, 9, 9), new Vector2(4f, 4f));

    private readonly PauseMenu _pauseMenu = new();

    /// <summary>The body the document placed, for the level that wants to reach it.</summary>
    protected Player Player { get; private set; } = null!;

    /// <summary>The track the room plays, which its document names.</summary>
    [Authorable(Required = true)]
    public AudioClip Music { get; private set; }

    // The display and the menu are added here, so their assets join the scene's preload.
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

        if (EntryPayload is Arrival arrival)
        {
            Entrance entrance = FindFirst<Entrance>(entrance => entrance.Key == arrival.Entrance)
                ?? throw new InvalidOperationException(
                    $"No entrance in this room has the key \"{arrival.Entrance}\". Place an entrance with that key, or fix the arriveAt that leads here.");
            Player.Teleport(entrance.Position);
        }

        // Crossfades from whatever was playing, or fades in alone under --scene scenes/room.
        Run.Game.Music.Play(Music);
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
