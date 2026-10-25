using Capsule.Bench.Logic.Entities;
using Capsule.Bench.Logic.UI;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>One frame that never changes, whose capture compares byte for byte between two builds of the renderer.</summary>
/// <remarks>
/// The capture cannot depend on the wall-clock alpha. <c>still.scene.json</c> places the world in draw
/// order. The screen layer is added here because a document cannot place a screen entity. Together
/// they cover every path the renderer has, and <c>--driver StillCapture</c> saves the frame. Each
/// further row adds one thing to the same frame. A capture that moves then names the path that moved it.
/// Row <c>StillLit</c> dims the frame by an ambient colour and adds one point light and one additive
/// world sprite near the middle, which exercises the light pass. Row <c>StillShaded</c> adds a strip of
/// sprites drawn through a game material that binds a texture of its own and reads its size. Row
/// <c>StillHd</c> draws the unlit frame on the linearly sampled 1080p surface.
/// </remarks>
[Workload(WorkloadKind.Rendering)]
[Workload(WorkloadKind.Rendering, Row = Lit)]
[Workload(WorkloadKind.Rendering, Row = Shaded)]
[Workload(WorkloadKind.Rendering, Surface.Hd1080, Row = Hd)]
public sealed class Still : Scene
{
    public const string Lit = "StillLit";

    public const string Shaded = "StillShaded";

    public const string Hd = "StillHd";

    public Still(SceneContent content)
        : base(content)
    {
        ClearColor = new ColorRgba(24, 28, 40);
        if (Workloads.Running == Lit)
        {
            Ambient = new ColorRgba(64, 68, 96);
            Add(new Glow(World.ViewportSize / 2f));
        }
        else if (Workloads.Running == Shaded)
        {
            AddShadedStrip();
        }

        Add(new Caption(Anchor.TopLeft, new Vector2(8f, 8f), "Capsule bench 0123 AaBb"));
        Add(new Panel(Anchor.BottomRight, new Vector2(-130f, -60f), new Vector2(120f, 48f)));
    }

    // Below the sprite field, one swatch per band of the ramp, each through a material of its own. Row
    // names a band's bottom texel row, the one opaque across the ramp.
    private void AddShadedStrip()
    {
        for (int row = 0; row < 8; row++)
        {
            Material material = new(CapsuleAssets.Shaders.ShadeShader);
            material.Set("Ramp", CapsuleAssets.Textures.StripTexture);
            material.Set("Row", (row * 16f) + 15f);
            Add(new Swatch(new Vector2(24f + (row * 24f), 280f), material));
        }
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
