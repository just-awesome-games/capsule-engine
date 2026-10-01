using System.Numerics;
using Capsule.Input;
using Capsule.Scenes;

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
        Assert.Throws<InvalidOperationException>(() => new InputScript().Down(MouseButton.Left).Tap(MouseButton.Left));
    }

    [Fact]
    public void AStickDirection_IsHeldReleasedAndTappedLikeAButton()
    {
        InputButton up = StickDirection.LeftStickUp;
        InputButton left = StickDirection.LeftStickLeft;
        List<DeviceSnapshot> steps = Steps(new InputScript()
            .Down(up)
            .Wait(1)
            .Up(up)
            .Tap(left)
            .Build());

        Assert.True(up.IsDown(steps[0]));
        Assert.False(up.IsDown(steps[1]));
        Assert.True(left.IsDown(steps[1]));
        Assert.Throws<InvalidOperationException>(() => new InputScript().Down(up).Tap(up));
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
    public void Scroll_TurnsTheWheelForExactlyOneStep()
    {
        List<DeviceSnapshot> steps = Steps(new InputScript()
            .Down(MouseButton.Left)
            .MoveTo(new Vector2(10f, 20f))
            .Scroll(new Vector2(0f, -2f))
            .Wait(1)
            .Build());

        Assert.Equal(2, steps.Count);
        Assert.Equal(new Vector2(0f, -2f), steps[0].Scroll);
        Assert.Equal(Vector2.Zero, steps[1].Scroll);
        Assert.All(
            steps,
            snapshot =>
            {
                Assert.True(snapshot.IsDown(MouseButton.Left));
                Assert.Equal(new Vector2(10f, 20f), snapshot.Pointer);
            });
    }

    [Fact]
    public void WindowFocus_IsHeldUntilTheScriptTakesItAway()
    {
        List<DeviceSnapshot> steps = Steps(new InputScript()
            .Wait(1)
            .WindowFocus(false)
            .Wait(1)
            .WindowFocus(true)
            .Wait(1)
            .Build());

        Assert.Equal([true, false, true], steps.Select(snapshot => snapshot.HasWindowFocus));
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
