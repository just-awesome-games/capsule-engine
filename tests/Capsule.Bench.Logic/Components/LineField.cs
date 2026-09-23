using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Bench.Logic.Components;

/// <summary>10 000 lines a frame from one renderer — a hundred fans of a hundred spokes — as a debug-draw-heavy frame submits them.</summary>
public sealed class LineField : Renderer
{
    private static readonly ColorRgba[] Colors =
    [
        ColorRgba.White,
        new ColorRgba(255, 96, 96),
        new ColorRgba(96, 255, 128),
        new ColorRgba(96, 160, 255),
    ];

    protected override void Draw(FrameView view)
    {
        for (int fan = 0; fan < 100; fan++)
        {
            Vector2 hub = new(32f + ((fan % 10) * 64f), 18f + ((fan / 10) * 36f));

            for (int spoke = 0; spoke < 100; spoke++)
            {
                // The spoke's end walks the perimeter of a square about the hub, so no sine is evaluated.
                float t = spoke / 25f;
                int side = (int)t;
                float along = ((t - side) * 2f) - 1f;
                Vector2 end = side switch
                {
                    0 => new Vector2(along, -1f),
                    1 => new Vector2(1f, along),
                    2 => new Vector2(-along, 1f),
                    _ => new Vector2(-1f, -along),
                };

                view.Add(new LineIntent(hub, hub + (end * 30f), spoke % 25 == 0 ? 2f : 0f, Colors[(fan + spoke) % Colors.Length]));
            }
        }
    }
}
