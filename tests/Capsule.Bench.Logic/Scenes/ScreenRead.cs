using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Components;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>What a material reading the picture costs over 2 000 moving tiles, read in <c>intervalMs</c> uncapped.</summary>
/// <remarks>
/// <c>None</c> rows draw the same quads with the engine's shader. <c>Strip</c> rows read a band one view wide and 32
/// pixels tall, the usual small effect. <c>View</c> rows read the whole view. <c>Off</c> reads a strip the camera never
/// sees, which must cost what <c>None</c> does. <c>Bands8</c> makes eight separate copies.
/// </remarks>
[Workload(WorkloadKind.Rendering, Row = None)]
[Workload(WorkloadKind.Rendering, Row = Strip)]
[Workload(WorkloadKind.Rendering, Row = View)]
[Workload(WorkloadKind.Rendering, Row = Off)]
[Workload(WorkloadKind.Rendering, Surface.Hd1080, Row = HdNone)]
[Workload(WorkloadKind.Rendering, Surface.Hd1080, Row = HdStrip)]
[Workload(WorkloadKind.Rendering, Surface.Hd1080, Row = HdView)]
[Workload(WorkloadKind.Rendering, Surface.Hd1080, Row = HdBands8)]
public sealed class ScreenRead : Scene
{
    public const string None = "ScreenReadNone";

    public const string Strip = "ScreenReadStrip";

    public const string View = "ScreenReadView";

    public const string Off = "ScreenReadOff";

    public const string HdNone = "ScreenReadHdNone";

    public const string HdStrip = "ScreenReadHdStrip";

    public const string HdView = "ScreenReadHdView";

    public const string HdBands8 = "ScreenReadHdBands8";

    private const float StripHeight = 32f;

    private readonly Material _ripple = new(CapsuleAssets.Shaders.RippleShader);
    private int _steps;

    public ScreenRead()
    {
        string? row = Workloads.Running;
        Vector2 view = row is HdNone or HdStrip or HdView or HdBands8 ? World.HdViewportSize : World.ViewportSize;
        Camera = new ParkedCamera(view / 2f, view);
        Add(new Holder(new SpriteField(SpriteField.Tile, 8f, view, 2_000, false)));

        switch (row)
        {
            case None or HdNone:
                Add(new Band(new Vector2(0f, view.Y / 2f), new Vector2(view.X, StripHeight), null));
                break;
            case Strip or HdStrip:
                Add(new Band(new Vector2(0f, view.Y / 2f), new Vector2(view.X, StripHeight), _ripple));
                break;
            case View or HdView:
                Add(new Band(Vector2.Zero, view, _ripple));
                break;
            case Off:
                Add(new Band(new Vector2(0f, -4096f), new Vector2(view.X, StripHeight), _ripple));
                break;
            default:
                // Each plain band between two readers ends a run. Every reader takes a copy of its own.
                for (int band = 0; band < 8; band++)
                {
                    Add(new Band(new Vector2(0f, (band + 1f) * view.Y / 10f), new Vector2(view.X, StripHeight), _ripple));
                    Add(new Band(new Vector2(0f, ((band + 1f) * view.Y / 10f) + StripHeight), new Vector2(view.X, 2f), null));
                }

                break;
        }
    }

    protected override void OnStep(in StepContext context) => _ripple.Set("Rise", ++_steps / 4);

    private sealed class Band : Entity
    {
        internal Band(Vector2 position, Vector2 size, Material? material)
            : base(position) => Add(new ColorRect(size) { Material = material });
    }
}
