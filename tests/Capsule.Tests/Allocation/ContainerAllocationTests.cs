using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.UI;
using Capsule.UI;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class ContainerAllocationTests
{
    // Every screen draw reads its slot, and a resize on any step re-solves the boxes above it. Neither
    // the step nor the frame built after it may allocate once the track storage has grown.
    [Fact]
    public void AContainerMenuResizedEveryStep_AllocatesNothingOnceWarm()
    {
        Column column = new();
        Scene scene = new();
        scene.Add(column);

        using SimulationHost run = new(scene, run: new Run { Canvas = new Vector2(320f, 180f) });

        StepSample[] samples = StepMeasurement.Measure(run.Simulation);

        Assert.All(samples, static sample => Assert.Equal((0L, 0L), (sample.StepBytes, sample.ViewBytes)));
        Assert.True(samples[^1].Render.Visible > 0);
    }

    // A flow sizes its storage for the children it holds when it first lays out. Showing one of them later
    // takes a cell without growing it.
    [Fact]
    public void AFlowChildShownAfterTheFirstLayout_AllocatesNothing()
    {
        FlowContainer flow = new(Anchor.TopLeft, Vector2.Zero);
        ColorRect[] cells = [.. Enumerable.Range(0, 5).Select(_ => ContainerTests.Leaf(flow, Anchor.TopLeft, new Vector2(4f, 4f)))];
        cells[4].Entity!.Visible = false;
        Scene scene = new();
        scene.Add(flow);

        using SimulationHost run = new(scene, run: new Run { Canvas = new Vector2(320f, 180f) });
        _ = cells[3].Bounds;
        cells[4].Entity!.Visible = true;

        long before = StepMeasurement.Start();
        Rect shown = cells[4].Bounds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(16f, shown.Left);
        Assert.Equal(0L, allocated);
    }

    // A column of rows under a gathering navigator. Each row is a caption cell and a bar, in a flow for
    // the first row and in a horizontal box for the rest. The flow's caption cell changes width every
    // step, which breaks its row and joins it again.
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
            Add(new FocusNavigator(FocusFixtures.Actions));

            ScreenEntity? first = null;
            for (int i = 0; i < 8; i++)
            {
                ScreenEntity row = i == 0
                    ? new FlowContainer(Anchor.TopLeft, Vector2.Zero) { Parent = this, MaxSize = new Vector2(120f, float.PositiveInfinity), Spacing = new Vector2(8f, 0f) }
                    : new BoxContainer(Axis.Horizontal, Anchor.TopWide, Vector2.Zero) { Parent = this, Spacing = 8f };
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
