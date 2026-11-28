using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

// The sprite intents the frame specs store, each an unturned, unflipped white draw at rest at one position,
// and the one step that draws a scene.
internal static class SpriteFixtures
{
    /// <summary>A 4x4 white quad whose top-left corner is <paramref name="position"/>.</summary>
    internal static SpriteIntent Quad(Vector2 position) =>
        new(Sprite.White, position, position, PreviousRotation: 0f, Rotation: 0f, new Vector2(4f, 4f), FlipX: false, FlipY: false, ColorRgba.White);

    /// <summary>An 8x8 frame of <see cref="SceneFixtures.Atlas"/> pivoted on its centre at <paramref name="position"/>.</summary>
    internal static SpriteIntent Centred(Vector2 position) =>
        Drawn(new Sprite(SceneFixtures.Atlas, new TextureRegion(0, 0, 8, 8), new Vector2(4, 4)), position, position);

    /// <summary><paramref name="frame"/> drawn at its region's size, moving from <paramref name="previous"/> to <paramref name="position"/>.</summary>
    internal static SpriteIntent Drawn(in Sprite frame, Vector2 previous, Vector2 position, float previousRotation = 0f, float rotation = 0f) =>
        new(
            frame,
            previous,
            position,
            previousRotation,
            rotation,
            new Vector2(frame.Region.Width, frame.Region.Height),
            FlipX: false,
            FlipY: false,
            ColorRgba.White);

    /// <summary>A simulation of a fresh scene holding <paramref name="entities"/>, stepped once.</summary>
    internal static SceneSimulation Stepped(params Entity[] entities) => Stepped(new Scene(), entities);

    /// <summary>A simulation of <paramref name="scene"/> holding <paramref name="entities"/>, stepped once.</summary>
    internal static SceneSimulation Stepped(Scene scene, params Entity[] entities)
    {
        SceneSimulation simulation = SceneFixtures.Simulation(scene, entities);
        simulation.Step(SceneFixtures.Step());

        return simulation;
    }
}
