using System.Numerics;
using Capsule.Bench.Logic.Entities;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// <see cref="HdField2k"/>'s 2 000 sprites, under 256 point lights drifting on a slow deterministic
/// path over a dim ambient. The row that proves the light pass is flat: its cost is lights times
/// sprites and nothing more.
/// </summary>
[Workload(WorkloadKind.Rendering, Surface.Hd1080)]
public sealed class Lights256 : SpriteFieldScene
{
    private const int LightCount = 256;

    public Lights256()
        : base(new Sprite(CapsuleAssets.Textures.HdAtlas, new TextureRegion(0, 0, 256, 256), new Vector2(128f, 128f)), 256f, World.HdViewportSize, count: 2_000)
    {
        Ambient = new ColorRgba(40, 42, 56);
        Add(new Holder(new LightField(World.HdViewportSize, LightCount)));
    }

    // One intent stream of lights on a 16 by 16 grid, drifting together, no entity stepping behind
    // it, as SpriteField draws its sprites.
    private sealed class LightField(Vector2 stage, int count) : Renderer
    {
        private const int Columns = 16;
        private const float Radius = 200f;

        private static readonly ColorRgba[] Tints =
        [
            new ColorRgba(255, 200, 140),
            new ColorRgba(140, 200, 255),
            new ColorRgba(200, 255, 160),
        ];

        private long _tick;

        protected override void OnStep(in StepContext context) => _tick = context.Tick;

        protected override void Draw(FrameView view)
        {
            int rows = (count + Columns - 1) / Columns;
            Vector2 cell = new(stage.X / Columns, stage.Y / rows);
            Vector2 previous = Drift(_tick - 1);
            Vector2 current = Drift(_tick);

            for (int i = 0; i < count; i++)
            {
                Vector2 anchor = new(((i % Columns) * cell.X) + (cell.X / 2f), ((i / Columns) * cell.Y) + (cell.Y / 2f));

                view.Add(new LightIntent(
                    Sprite.Light,
                    anchor + previous,
                    anchor + current,
                    PreviousRotation: 0f,
                    Rotation: 0f,
                    Radius,
                    Tints[i % Tints.Length]));
            }
        }

        // A slow triangle wave over 480 ticks, half SpriteField's rate so the two drifts never phase-lock.
        private static Vector2 Drift(long tick)
        {
            long phase = ((tick % 480) + 480) % 480;
            float t = phase < 240 ? phase : 480 - phase;

            return new Vector2(t * 0.1f, t * 0.05f);
        }
    }
}
