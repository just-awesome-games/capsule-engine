using System.Numerics;
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
    [Fact]
    public void MoveTo_PutsThePointerOnThatCanvasPositionFromTheNextStepOn()
    {
        List<DeviceSnapshot> steps = Steps(new InputScript()
            .MoveTo(new Vector2(12f, 34f))
            .Wait(1)
            .Tap(MouseButton.Left)
            .MoveTo(new Vector2(-5f, 34f))
            .Wait(1)
            .Build());

        Assert.Equal(3, steps.Count);
        Assert.All(steps, snapshot => Assert.Equal(34f, snapshot.Pointer.Y));
        Assert.Equal(new Vector2(12f, 34f), steps[0].Pointer);

        // The tap's own step carries the click, and the pointer edit after it lands on the step after.
        Assert.False(steps[0].IsDown(MouseButton.Left));
        Assert.True(steps[1].IsDown(MouseButton.Left));
        Assert.Equal(new Vector2(12f, 34f), steps[1].Pointer);
        Assert.Equal(new Vector2(-5f, 34f), steps[2].Pointer);
        Assert.False(steps[2].IsDown(MouseButton.Left));
    }

    [Fact]
    public void AMouseButton_IsHeldAndReleasedLikeAnyOther()
    {
        List<DeviceSnapshot> steps = Steps(new InputScript()
            .Down(MouseButton.Right)
            .Wait(1)
            .Up(MouseButton.Right)
            .Wait(1)
            .Build());

        Assert.True(steps[0].IsDown(MouseButton.Right));
        Assert.False(steps[1].IsDown(MouseButton.Right));
    }

    [Fact]
    public void TappingAHeldMouseButton_IsRefused()
    {
        InputScript script = new InputScript().Down(MouseButton.Left);

        Assert.Throws<InvalidOperationException>(() => script.Tap(MouseButton.Left));
    }

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
