using Capsule.Bench.Logic.UI;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>One frame that never changes, whose capture compares byte for byte between two builds of the renderer.</summary>
/// <remarks>
/// The capture cannot depend on the wall-clock alpha. <c>still.scene.json</c> places the world in draw
/// order. The screen layer is added here because a document cannot place a screen entity. Together
/// they cover every path the renderer has, and <c>--driver StillCapture</c> saves the frame. Row
/// <c>StillLit</c> dims the same frame by an ambient colour and adds one point light and one additive
/// world sprite near the middle. Its capture exercises the light pass the way <c>Still</c>'s exercises
/// the unlit renderer.
/// </remarks>
[Workload(WorkloadKind.Rendering)]
[Workload(WorkloadKind.Rendering, Row = Lit)]
public sealed class Still : Scene
{
    private const string Lit = "StillLit";

    public Still(SceneContent content)
        : base(content)
    {
        ClearColor = new ColorRgba(24, 28, 40);
        if (Workloads.Running == Lit)
        {
            Ambient = new ColorRgba(64, 68, 96);
            Add(new Glow(World.ViewportSize / 2f));
        }

        Add(new Caption(Anchor.TopLeft, new Vector2(8f, 8f), "Capsule bench 0123 AaBb"));
        Add(new Panel(Anchor.BottomRight, new Vector2(-130f, -60f), new Vector2(120f, 48f)));
    }

    // A point light beside one additive sprite. The capture covers both light-map paths: an
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
