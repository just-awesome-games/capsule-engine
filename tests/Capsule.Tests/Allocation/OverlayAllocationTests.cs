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

// The open overlay allocates nothing while idle: rows, readout and root hotkey rows rebuild only on
// a host act, never on a frame nothing changed.
[Collection(StageAllocationCollection.Name)]
public sealed class OverlayAllocationTests
{
    [Fact]
    public void OpenAtTheRootWithTheFramePaneOn_AnIdleStretchAllocatesNothing()
    {
        using OverlayRig rig = new();
        rig.Open();
        rig.Overlay.ToggleFramePane();

        for (int i = 0; i < 60; i++)
        {
            rig.Frame(16, 1, sampled: DeviceSnapshot.Empty);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 300; i++)
        {
            rig.Frame(16, 1, sampled: DeviceSnapshot.Empty);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // Open on an entity panel whose OnDebugPanel writes a long, a float, a Vector2 and a command. The
    // same idle stretch allocates nothing, and the one Step act afterwards is not stale. The readout
    // and the field both show what the step just produced, in the same fact as the zero-allocation
    // claim.
    [Fact]
    public void OpenOnAnEntityPanel_AnIdleStretchAllocatesNothingAndOneStepStaysFresh()
    {
        Instrumented scene = new();
        using SceneHost host = new(
            SceneTransition.ToScene(typeof(Instrumented), null),
            (in SceneTransition _) => scene,
            new Run());
        FixedStepScheduler scheduler = CreateScheduler();
        using OverlayHost overlay = new(Key.Grave, scheduler, host, host);

        Open(overlay, scheduler, host);
        Press(overlay, scheduler, host, Key.S);
        Press(overlay, scheduler, host, Key.Enter);

        Assert.Equal("Holder", overlay.Title);
        Assert.Contains(overlay.Scene.ShownRows(), static row => row.StartsWith("Ticks", StringComparison.Ordinal));

        for (int i = 0; i < 60; i++)
        {
            Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 300; i++)
        {
            Frame(overlay, scheduler, host, DeviceSnapshot.Empty);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        Press(overlay, scheduler, host, Key.Right);

        Assert.Equal(1, scheduler.Tick);
        Assert.Equal("Instrumented  tick 1", overlay.Readout);
        Assert.Contains(
            overlay.Scene.ShownRows(),
            static row => row.StartsWith("Ticks", StringComparison.Ordinal) && row.EndsWith('1'));
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
