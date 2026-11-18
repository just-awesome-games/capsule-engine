using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// <see cref="HdField2k"/>'s 2 000 sprites, under 256 point lights drifting on a slow deterministic
/// path over a dim ambient. The row that proves the light pass is flat: its cost is lights times
/// sprites and nothing more.
/// </summary>
/// <remarks>
/// Row <c>ParticleLights256</c> draws the same lights on the same path from one light-only
/// <see cref="ParticleEmitter"/>, in one tint: the CPU cost of a light per particle against one renderer's stream.
/// </remarks>
[Workload(WorkloadKind.Rendering, Surface.Hd1080)]
[Workload(WorkloadKind.Rendering, Surface.Hd1080, Row = Particles)]
public sealed class Lights256 : SpriteFieldScene
{
    private const string Particles = "ParticleLights256";
    private const int LightCount = 256;
    private const int Columns = 16;
    private const float Radius = 200f;

    public Lights256()
        : base(new Sprite(CapsuleAssets.Textures.HdAtlasTexture, new TextureRegion(0, 0, 256, 256), new Vector2(128f, 128f)), 256f, World.HdViewportSize, count: 2_000)
    {
        Ambient = new ColorRgba(40, 42, 56);
        if (Workloads.Running == Particles)
        {
            Add(new ParticleField(World.HdViewportSize, LightCount));
        }
        else
        {
            Add(new Holder(new LightField(World.HdViewportSize, LightCount)));
        }
    }

    // The centre of light i's cell on the 16 by 16 grid over the stage.
    private static Vector2 Anchor(Vector2 stage, int count, int i)
    {
        int rows = (count + Columns - 1) / Columns;
        Vector2 cell = new(stage.X / Columns, stage.Y / rows);

        return new(((i % Columns) * cell.X) + (cell.X / 2f), ((i / Columns) * cell.Y) + (cell.Y / 2f));
    }

    // A slow triangle wave over 480 ticks, half SpriteField's rate so the two drifts never phase-lock.
    private static Vector2 Drift(long tick)
    {
        long phase = ((tick % 480) + 480) % 480;
        float t = phase < 240 ? phase : 480 - phase;

        return new Vector2(t * 0.1f, t * 0.05f);
    }

    // One still particle on each grid point of a local emitter that outlives the run, its entity drifting the grid.
    private sealed class ParticleField : Entity
    {
        internal ParticleField(Vector2 stage, int count)
            : base(Drift(-1))
        {
            ParticleEmitter emitter = new(capacity: count)
            {
                Space = ParticleSpace.Local,
                Lifetime = 3_600f,
                Color = Gradient.Constant(new ColorRgba(255, 200, 140)),
                Light = new ParticleLight(Radius),
            };
            Add(emitter);
            for (int i = 0; i < count; i++)
            {
                emitter.Emit(1, Anchor(stage, count, i));
            }
        }

        protected override void OnStep(in StepContext context) => Position = Drift(context.Tick);
    }

    // One intent stream of lights on a 16 by 16 grid, drifting together, no entity stepping behind
    // it, as SpriteField draws its sprites.
    private sealed class LightField(Vector2 stage, int count) : Renderer
    {
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
            Vector2 previous = Drift(_tick - 1);
            Vector2 current = Drift(_tick);

            for (int i = 0; i < count; i++)
            {
                Vector2 anchor = Anchor(stage, count, i);

                view.Add(new LightIntent(
                    Sprite.Light,
                    anchor + previous,
                    anchor + current,
                    PreviousRotation: 0f,
                    Rotation: 0f,
                    new Vector2(2f * Radius),
                    FlipX: false,
                    FlipY: false,
                    Tints[i % Tints.Length]));
            }
        }
    }
}
