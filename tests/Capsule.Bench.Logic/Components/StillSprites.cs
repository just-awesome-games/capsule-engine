using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Bench.Logic.Components;

/// <summary>Two hundred sprites that never move — quarters of the tile, turned, flipped, tinted, a few stretched — so the still frame is submission alone.</summary>
public sealed class StillSprites : Renderer
{
    private const int Columns = 20;

    private const int Rows = 10;

    protected override void Draw(FrameView view)
    {
        for (int i = 0; i < Columns * Rows; i++)
        {
            int quadrant = i % 4;
            Sprite frame = new(
                CapsuleAssets.Textures.Tile,
                new TextureRegion((quadrant & 1) * 16, (quadrant >> 1) * 16, 16, 16),
                new Vector2(8f, 8f));
            Vector2 position = new(24f + ((i % Columns) * 24f), 24f + ((i / Columns) * 24f));
            float angle = quadrant switch
            {
                1 => 0.5f,
                3 => 2.2f,
                _ => 0f,
            };

            view.Add(new SpriteIntent(
                frame,
                position,
                position,
                angle,
                angle,
                i % 9 == 0 ? new Vector2(24f, 12f) : new Vector2(16f, 16f),
                FlipX: i % 3 == 0,
                FlipY: i % 5 == 0,
                i % 6 == 0 ? SpriteField.Tints[(i / 6) % SpriteField.Tints.Length] : ColorRgba.White));
        }
    }
}
