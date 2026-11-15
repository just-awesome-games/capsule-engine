using System.Runtime.CompilerServices;
using Capsule.Input;
using Capsule.Scenes;
using static Capsule.Tests.DevTools.PanelFixtures;

namespace Capsule.Tests.DevTools;

// What the overlay holds of a scene the run has left, which is nothing. Reachability is asked of the
// scene itself, as SceneTransitionRetentionTests asks it.
public sealed class OverlayRetentionTests
{
    // The overlay closes over an entity's panel whose command it ran, and the run then leaves the scene
    // by itself. The page, the panel's rows and the pointer all named that scene.
    [Fact]
    public void AnOverlayClosedOverAPanelItRanACommandFrom_HoldsNothingOfTheSceneTheRunLeaves()
    {
        using OverlayRig rig = new(CreateHost());

        Reachability.AssertReleased(RunCommandThenLeave(rig));
    }

    // The scene left and its entities. Nothing on this stack outlives the call to keep them alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RunCommandThenLeave(OverlayRig rig)
    {
        Scene scene = rig.Host.Scene;
        WeakReference[] left = [new(scene), .. scene.Entities.ToArray().Select(static entity => new WeakReference(entity))];
        rig.OpenEntityPanel("Lone");
        rig.SelectRow("[x] Visible");
        rig.Press(Key.Grave);

        rig.Host.Run.RequestScene<OtherScene>();
        rig.Frame();

        Assert.IsType<OtherScene>(rig.Host.Scene);

        return left;
    }
}
