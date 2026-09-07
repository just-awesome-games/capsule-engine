using Capsule.Input;
using Capsule.Scenes;
using Capsule.Scenes.Input;

namespace Capsule.Tests.Input;

public sealed class InputScriptTests
{
    [Fact]
    public void Tap_HoldsForExactlyOneStepOnTopOfWhatIsAlreadyHeld()
    {
        List<DeviceSnapshot> steps = Steps(new InputScript()
            .Down(Key.LeftShift)
            .Wait(1)
            .Tap(Key.Space)
            .Wait(1)
            .Build());

        Assert.Equal(3, steps.Count);
        Assert.False(steps[0].IsDown(Key.Space));
        Assert.True(steps[1].IsDown(Key.Space));
        Assert.False(steps[2].IsDown(Key.Space));
        Assert.All(steps, snapshot => Assert.True(snapshot.IsDown(Key.LeftShift)));
    }

    [Fact]
    public void Wait_EmitsTheHeldStateAndEditsEmitNothingOfTheirOwn()
    {
        List<DeviceSnapshot> steps = Steps(new InputScript()
            .Down(Key.A)
            .Down(PadButton.LeftShoulder)
            .Axis(PadAxis.LeftStickY, 0.5f)
            .Up(Key.A)
            .Wait(2)
            .Build());

        Assert.Equal(2, steps.Count);
        Assert.All(
            steps,
            snapshot =>
            {
                Assert.False(snapshot.IsDown(Key.A));
                Assert.True(snapshot.IsDown(PadButton.LeftShoulder));
                Assert.Equal(0.5f, snapshot.Axis(PadAxis.LeftStickY));
            });
    }

    [Fact]
    public void Tap_OfSomethingAlreadyHeld_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => new InputScript().Down(Key.Space).Tap(Key.Space));
        Assert.Throws<InvalidOperationException>(() => new InputScript().Down(PadButton.South).Tap(PadButton.South));
    }

    [Fact]
    public void Wait_RejectsANegativeStepCountAndEmitsNothingForZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InputScript().Wait(-1));
        Assert.Empty(Steps(new InputScript().Down(Key.A).Wait(0).Build()));
    }

    // Every step the driver drives, in step order, which is the sequence the script emitted.
    private static List<DeviceSnapshot> Steps(IInputDriver driver)
    {
        Blank scene = new();
        List<DeviceSnapshot> steps = [];

        for (long tick = 0; driver.TryNext(scene, tick, out DeviceSnapshot snapshot); tick++)
        {
            steps.Add(snapshot);
        }

        return steps;
    }

    private sealed class Blank : Scene;
}
