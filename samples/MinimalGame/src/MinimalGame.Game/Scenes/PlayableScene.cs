using Capsule;
using Capsule.Assets.Generated;
using Capsule.Scenes;
using MinimalGame.Game.Cameras;
using MinimalGame.Game.Entities;
using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// What every playable scene is made of, with the level left to the subclass: the follow camera it is
/// framed through, the head-up display over it, quitting, and returning to the <see cref="MainMenu"/>
/// at no health. A subclass adds the document it claims and nothing else, so a level is one line. Both
/// the camera and the display are installed in the constructor, so the scene opens on that camera and
/// the display's contents are collected for its preload.
/// </summary>
public abstract class PlayableScene : Scene
{
    /// <summary>The body the document placed, for the level that wants to reach it.</summary>
    protected Player Player { get; private set; } = null!;

    protected PlayableScene(SceneContent content)
        : base(content)
    {
        Camera = new GameCamera();
        Add(new PlayerHud());
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        Player = FindSingle<Player>();

        // Crossfades from whatever was playing, or fades in alone under --scene Room.
        Run.Game.Music.Play(CapsuleAssets.Audio.Music.Room);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Quit))
        {
            Run.RequestExit();
        }
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
