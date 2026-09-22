using Capsule;
using Capsule.Scenes;
using MinimalGame.Game.Entities;
using MinimalGame.Game.UI;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// What every playable scene is made of, with the level left to the document: the head-up display,
/// quitting, and returning to the <see cref="MainMenu"/> at no health. A level is a document that names
/// it as its <c>baseScene</c>. The camera comes from the document, and the
/// display is installed in the constructor so its contents are collected for its preload.
/// </summary>
public abstract class PlayableScene : Scene
{
    /// <summary>The room's spark pool, as far up as sparks reach and no further.</summary>
    public EntityPool<SparkBurst> Sparks { get; } = new(() => new SparkBurst(), capacity: 8);

    /// <summary>The body the document placed, for the level that wants to reach it.</summary>
    protected Player Player { get; private set; } = null!;

    protected PlayableScene(SceneContent content)
        : base(content) =>
        Add(new PlayerHud());

    /// <inheritdoc/>
    protected override void OnStart()
    {
        Player = FindSingle<Player>();

        // Crossfades from whatever was playing, or fades in alone under --scene room.
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
