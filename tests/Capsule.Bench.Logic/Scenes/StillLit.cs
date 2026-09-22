using System.Numerics;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.UI;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// <see cref="Still"/> lit: the same frame, dimmed by an ambient colour, with one point light and one
/// additive world sprite near the middle. Its capture exercises the light pass the way <c>Still</c>'s
/// exercises the unlit renderer.
/// </summary>
[Workload(WorkloadKind.Rendering)]
public sealed class StillLit : Scene
{
    public StillLit(SceneContent content)
        : base(content)
    {
        Camera = new ParkedCamera();
        ClearColor = new ColorRgba(24, 28, 40);
        Ambient = new ColorRgba(64, 68, 96);

        Add(new Glow(World.ViewportSize / 2f));

        Add(new Caption(Anchor.TopLeft, new Vector2(8f, 8f), "Capsule bench 0123 AaBb"));
        Add(new Panel(Anchor.BottomRight, new Vector2(-130f, -60f), new Vector2(120f, 48f)));
    }

    // A point light beside one additive sprite, so the capture covers both light-map paths: an
    // authored light and rule 2's additive-draw-is-a-light.
    private sealed class Glow : Entity
    {
        internal Glow(Vector2 position)
            : base(position)
        {
            Add(new ColorRect(new Vector2(6f, 6f))
            {
                Color = new ColorRgba(255, 200, 140),
                Blend = BlendMode.Additive,
            });
            Add(new PointLight { Radius = 56f, Color = new ColorRgba(255, 200, 140) });
        }
    }
}
