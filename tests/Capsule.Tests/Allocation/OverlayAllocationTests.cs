using System.Numerics;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.DevTools;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Runtime;
using static Capsule.Tests.Runtime.OverlayRig;

namespace Capsule.Tests.Allocation;

// An idle overlay frame allocates nothing. The page is rebuilt only after a host act.
[Collection(StageAllocationCollection.Name)]
public sealed class OverlayAllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithTheFramePaneOn_AnIdleStretchAllocatesNothing(bool open)
    {
        using OverlayRig rig = new();
        if (open)
        {
            rig.Open();
        }

        rig.Overlay.ToggleFramePane();
        AssertIdleStretchAllocatesNothing(rig);
        Assert.Equal(62.5, rig.Overlay.Scene.Pane.Figures.Fps, 1);
    }

    // The panel's hook writes a long, a float, a Vector2 and a command. The Step after the idle
    // stretch still refreshes the readout and the field.
    [Fact]
    public void OpenOnAnEntityPanel_AnIdleStretchAllocatesNothingAndOneStepStaysFresh()
    {
        using OverlayRig rig = new(new SceneHost(
            SceneTransition.ToScene(typeof(Instrumented), null),
            static (in SceneTransition _) => new Instrumented(),
            new Run()));

        rig.Open();
        rig.Press(Key.S);
        rig.Press(Key.Enter);

        Assert.Equal("Holder", rig.Overlay.Title);
        Assert.Contains(rig.Overlay.Scene.ShownRows(), static row => row.StartsWith("Ticks", StringComparison.Ordinal));

        AssertIdleStretchAllocatesNothing(rig);
        rig.Press(Key.Right);

        Assert.Equal(1, rig.Scheduler.Tick);
        Assert.Equal("Instrumented  tick 1", rig.Overlay.Scene.Readout);
        Assert.Contains(
            rig.Overlay.Scene.ShownRows(),
            static row => row.StartsWith("Ticks", StringComparison.Ordinal) && row.EndsWith('1'));
    }

    private static void AssertIdleStretchAllocatesNothing(OverlayRig rig)
    {
        for (int frame = 0; frame < 60; frame++)
        {
            rig.Frame();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 300; frame++)
        {
            rig.Frame();
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class Instrumented : Scene
    {
        internal Instrumented() => Add(new Holder(Vector2.Zero));
    }

    private sealed class Holder(Vector2 position) : Entity(position)
    {
        private long _ticks;

        protected internal override void OnStep(in StepContext context) => _ticks++;

        protected internal override void OnDebugPanel(DebugPanel panel)
        {
            panel.Field("Ticks", _ticks);
            panel.Field("Speed", 1.5f);
            panel.Field("Position", Position);
            panel.Command("Nudge", () => Position += Vector2.UnitX);
        }
    }
}
