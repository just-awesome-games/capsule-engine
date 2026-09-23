using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Bench.Logic.Components;

public sealed class ParallaxRow : Renderer
{
    protected override void Draw(FrameView view)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 position = new(20f + (i * 40f), 300f);

            view.Add(new SpriteIntent(SpriteField.Tile, position, position, 0f, 0f, new Vector2(16f, 16f), FlipX: false, FlipY: false, ColorRgba.White));
        }
    }
}
