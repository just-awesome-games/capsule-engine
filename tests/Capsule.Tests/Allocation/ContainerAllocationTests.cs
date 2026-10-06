using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class ContainerAllocationTests
{
    private static readonly FocusActions Actions = new(new("Up"), new("Down"), new("Left"), new("Right"), new("Confirm"));

    // Every screen draw reads its slot, and a resize on any step re-solves the boxes above it. Neither
    // the step nor the frame built after it may allocate once the track storage has grown.
    [Fact]
    public void AContainerMenuResizedEveryStep_AllocatesNothingOnceWarm()
    {
        Column column = new();
        Scene scene = new();
        scene.Add(column);

        using SimulationHost run = new(scene, run: new Run { Canvas = new Vector2(320f, 180f) });

        StepSample[] samples = StepMeasurement.Measure(run.Simulation, run.StepSeconds, warmupSteps: 120, measuredSteps: 600);

        Assert.All(samples, static sample => Assert.Equal((0L, 0L), (sample.StepBytes, sample.ViewBytes)));
        Assert.True(samples[^1].Render.Visible > 0);
    }

    // A column of rows under a gathering navigator. Each row is a caption cell and a bar in a
    // horizontal box. One caption cell changes width every step.
    private sealed class Column : BoxContainer
    {
        private readonly ScreenEntity _resized;
        private bool _wide;

        internal Column()
            : base(Axis.Vertical, Anchor.Center, Vector2.Zero)
        {
            Spacing = 4f;
            Padding = new Insets(8f);
            Add(new ColorRect());
            Add(new FocusNavigator(Actions));

            ScreenEntity? first = null;
            for (int i = 0; i < 8; i++)
            {
                BoxContainer row = new(Axis.Horizontal, Anchor.TopWide, Vector2.Zero) { Parent = this, Spacing = 8f };
                row.Add(new Focusable());

                ScreenEntity caption = new(Anchor.Left, Vector2.Zero) { Parent = row, Size = new Vector2(40f, 16f) };
                caption.Add(new Label(BitmapFont.Default, "Row"));
                first ??= caption;

                ScreenEntity bar = new(Anchor.Left, Vector2.Zero) { Parent = row, Size = new Vector2(60f, 8f) };
                bar.Add(new ColorRect());
            }

            _resized = first!;
        }

        protected internal override void OnStep(in StepContext context)
        {
            _wide = !_wide;
            _resized.Size = new Vector2(_wide ? 56f : 40f, 16f);
        }
    }
}
