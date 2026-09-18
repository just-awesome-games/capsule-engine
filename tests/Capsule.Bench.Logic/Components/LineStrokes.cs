using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Bench.Logic.Components;

// No engine component draws a line, so the still frame's three are added here.
public sealed class LineStrokes : Renderer
{
    public override void Draw(FrameView view)
    {
        view.Add(new LineIntent(new Vector2(10f, 300f), new Vector2(200f, 340f), ColorRgba.White));
        view.Add(new LineIntent(new Vector2(300f, 20f), new Vector2(600f, 120f), 3f, new ColorRgba(255, 64, 64)));
        view.Add(new LineIntent(new Vector2(480f, 290f), new Vector2(480f, 350f), new ColorRgba(64, 255, 64)));
    }
}
