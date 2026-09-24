using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Components;

/// <summary>
/// A grid of sprites over a stage from one renderer, as a tile map draws a layer: a rendering
/// workload measures submission, so the whole field is one intent stream with no entity stepping
/// behind it. A quarter are turned at fixed angles, some flipped, some tinted, and all drift
/// together so every one interpolates; with <paramref name="switches"/> every fiftieth is the white
/// texel, which is the unatlased game's texture churn.
/// </summary>
public sealed class SpriteField(Sprite frame, float extent, Vector2 stage, int count, bool switches = false) : Renderer
{
    public static readonly Sprite Tile = new(CapsuleAssets.Textures.TileTexture, new TextureRegion(0, 0, 32, 32), new Vector2(16f, 16f));

    public static readonly ColorRgba[] Tints =
    [
        new ColorRgba(255, 128, 128),
        new ColorRgba(128, 255, 128),
        new ColorRgba(128, 160, 255, 200),
        new ColorRgba(255, 255, 128, 128),
    ];

    private static readonly float[] Angles = [0.3f, 0.7f, 1.1f, 1.9f, 2.6f];

    private readonly int _columns = Columns(count, stage);
    private long _tick;

    protected override void OnStep(in StepContext context) => _tick = context.Tick;

    protected override void Draw(FrameView view)
    {
        int rows = (count + _columns - 1) / _columns;
        Vector2 cell = new(stage.X / _columns, stage.Y / rows);
        Vector2 previous = Drift(_tick - 1);
        Vector2 current = Drift(_tick);
        Vector2 size = new(extent, extent);

        for (int i = 0; i < count; i++)
        {
            Vector2 anchor = new(((i % _columns) * cell.X) + 4f, ((i / _columns) * cell.Y) + 4f);
            float angle = (i & 3) == 0 ? Angles[i % 5] : 0f;

            view.Add(new SpriteIntent(
                switches && i % 50 == 0 ? Sprite.White : frame,
                anchor + previous,
                anchor + current,
                angle,
                angle,
                size,
                FlipX: i % 3 == 0,
                FlipY: i % 7 == 0,
                i % 10 == 0 ? Tints[(i / 10) % Tints.Length] : ColorRgba.White));
        }
    }

    // As many columns as keeps the cells near the stage's aspect.
    private static int Columns(int count, Vector2 stage)
    {
        int columns = 1;
        while (columns * columns * stage.Y < count * stage.X)
        {
            columns++;
        }

        return columns;
    }

    // A triangle wave over 240 ticks: successive ticks never agree, so interpolation always runs.
    private static Vector2 Drift(long tick)
    {
        long phase = ((tick % 240) + 240) % 240;
        float t = phase < 120 ? phase : 240 - phase;

        return new Vector2(t * 0.05f, t * 0.025f);
    }
}
