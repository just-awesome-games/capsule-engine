using Capsule;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace MinimalGame.Game;

/// <summary>
/// The boot scene, and the one backed by no <c>*.scene.json</c> and no <c>*.tmj</c>. Its public
/// parameterless constructor is what marks it class-only: <c>RunScene&lt;MainMenu&gt;()</c> builds it
/// as it is, with no document composed into it. It draws nothing on purpose — it holds no entities,
/// and says what it wants through the log.
/// </summary>
public sealed class MainMenu : Scene
{
    /// <inheritdoc/>
    protected override void OnStart() => Log.Info("Main menu: press Confirm to enter the room, Quit to leave.");

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Confirm))
        {
            RequestScene<Room>();
        }
        else if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }
}
