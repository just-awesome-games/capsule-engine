using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// 20 000 drifting sprites that take turns among eight handles packed on one atlas page: what a frame
/// pays to change handle on every sprite while the texture and the draw stay the same.
/// </summary>
[Workload(WorkloadKind.Rendering)]
public sealed class PackedHandles20k : Scene
{
    public PackedHandles20k()
    {
        Camera = new ParkedCamera(World.ViewportSize / 2f, World.ViewportSize);
        Add(new Holder(new Field()));
    }

    private sealed class Field : Renderer
    {
        private const int Columns = 189;

        private readonly int _count = Workloads.Scaled(20_000);

        private readonly Sprite[] _frames =
        [
            .. Enumerable.Range(0, 8).Select(static index => new Sprite(new TextureHandle($"packed/p-{index}", ".png"), new TextureRegion(0, 0, 32, 32), new Vector2(16f, 16f))),
        ];

        private long _tick;

        protected override void OnStep(in StepContext context) => _tick = context.Tick;

        protected override void Draw(FrameView view)
        {
            int rows = (_count + Columns - 1) / Columns;
            Vector2 cell = new(World.ViewportSize.X / Columns, World.ViewportSize.Y / rows);
            float previous = (_tick - 1) % 120 * 0.05f;
            float current = _tick % 120 * 0.05f;
            Vector2 size = new(8f, 8f);

            for (int index = 0; index < _count; index++)
            {
                Vector2 anchor = new(((index % Columns) * cell.X) + 4f, ((index / Columns) * cell.Y) + 4f);
                view.Add(new SpriteIntent(
                    _frames[index % _frames.Length],
                    anchor + new Vector2(previous, 0f),
                    anchor + new Vector2(current, 0f),
                    0f,
                    0f,
                    size,
                    FlipX: false,
                    FlipY: false,
                    ColorRgba.White));
            }
        }
    }
}
