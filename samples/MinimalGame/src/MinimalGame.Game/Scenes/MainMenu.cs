using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The boot scene, and the one backed by no <c>*.scene.json</c>. Its public
/// parameterless constructor is what marks it class-only: <c>RunScene&lt;MainMenu&gt;()</c> builds it
/// as it is, with no document composed into it. It draws one thing — the prompt, on an entity it
/// adds in code.
/// </summary>
public sealed class MainMenu : Scene
{
    private const string Prompt = "Press Confirm to enter the room,\nQuit to leave.";

    /// <inheritdoc/>
    protected override void OnStart()
    {
        // The other half of the camera model: a scene with nothing to follow spans the plain
        // camera it is given rather than installing one of its own, so its view is centred on the
        // world origin.
        Camera.ViewportSize = World.ViewportSize;

        Add(new PromptText(Vector2.Zero, Prompt));
    }

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

    // Not spawnable: it carries no EntitySpawn constructor, so no document can name it and the
    // scene that wants it adds it itself.
    private sealed class PromptText : Entity
    {
        internal PromptText(Vector2 center, string text)
            : base(center)
        {
            BitmapFont font = CapsuleAssets.Fonts.Menu;

            // A label's origin is its first line's top-left corner, so half the measured run left
            // and up of the entity centres the text on it.
            Add(new Label(font, text) { Offset = font.Measure(text) / -2f });
        }
    }
}
