using System.Numerics;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

// The sprite intents the frame specs store: an unturned, unflipped white draw at rest at one position.
internal static class SpriteFixtures
{
    /// <summary>A 4x4 white quad whose top-left corner is <paramref name="position"/>.</summary>
    internal static SpriteIntent Quad(Vector2 position) =>
        new(Sprite.White, position, position, PreviousRotation: 0f, Rotation: 0f, new Vector2(4f, 4f), FlipX: false, FlipY: false, ColorRgba.White);

    /// <summary>An 8x8 frame of <see cref="SceneFixtures.Atlas"/> pivoted on its centre at <paramref name="position"/>.</summary>
    internal static SpriteIntent Centred(Vector2 position) =>
        new(
            new Sprite(SceneFixtures.Atlas, new TextureRegion(0, 0, 8, 8), new Vector2(4, 4)),
            position,
            position,
            PreviousRotation: 0f,
            Rotation: 0f,
            new Vector2(8, 8),
            FlipX: false,
            FlipY: false,
            ColorRgba.White);
}
