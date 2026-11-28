using System.Numerics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Input;

public sealed class ActiveDeviceTests
{
    [Fact]
    public void TheSeed_IsReadOnTheFirstStepWithNoChange()
    {
        InputState input = new(new ActionBindings());
        input.Seed(InputDevice.Gamepad);

        input.Advance(DeviceSnapshot.Empty);

        Assert.Equal(InputDevice.Gamepad, input.ActiveDevice);
        Assert.False(input.ActiveDeviceChanged);
    }

    [Fact]
    public void AKeyPress_FlipsToTheKeyboardForTheStepItLandsOn()
    {
        InputState input = new(new ActionBindings());
        input.Seed(InputDevice.Gamepad);

        input.Advance(DeviceSnapshot.Of(Key.Space));
        Assert.Equal(InputDevice.KeyboardMouse, input.ActiveDevice);
        Assert.True(input.ActiveDeviceChanged);

        // Still held: neither a second change nor a flip back.
        input.Advance(DeviceSnapshot.Of(Key.Space));
        Assert.Equal(InputDevice.KeyboardMouse, input.ActiveDevice);
        Assert.False(input.ActiveDeviceChanged);
    }

    [Fact]
    public void APadButtonOrAxis_FlipsToTheGamepadForTheStepItLandsOn()
    {
        InputState input = new(new ActionBindings());

        input.Advance(DeviceSnapshot.Empty.With(PadButton.South));
        Assert.Equal(InputDevice.Gamepad, input.ActiveDevice);
        Assert.True(input.ActiveDeviceChanged);

        input.Advance(DeviceSnapshot.Empty.With(PadButton.South));
        Assert.False(input.ActiveDeviceChanged);

        input.Advance(DeviceSnapshot.Of(Key.Space));
        Assert.Equal(InputDevice.KeyboardMouse, input.ActiveDevice);

        input.Advance(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, 0.4f));
        Assert.Equal(InputDevice.Gamepad, input.ActiveDevice);
        Assert.True(input.ActiveDeviceChanged);
    }

    [Fact]
    public void PointerMotion_FlipsToTheMouseOnlyPastTheActivationDistance()
    {
        InputState input = new(new ActionBindings());
        input.Seed(InputDevice.Gamepad);

        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(1.5f, 0f)));
        Assert.Equal(InputDevice.Gamepad, input.ActiveDevice);

        input.Advance(DeviceSnapshot.Empty.WithPointer(new Vector2(4.5f, 0f)));
        Assert.Equal(InputDevice.KeyboardMouse, input.ActiveDevice);
        Assert.True(input.ActiveDeviceChanged);
    }

    [Fact]
    public void BothDevicesInOneStep_GiveTheGamepad()
    {
        InputState input = new(new ActionBindings());

        input.Advance(DeviceSnapshot.Of(Key.Space).With(PadButton.South));

        Assert.Equal(InputDevice.Gamepad, input.ActiveDevice);
    }

    // The driven path: a scripted pad press reaches the scene's step as an active-device change, with
    // the keyboard seed a headless run starts on.
    [Fact]
    public void ADrivenRun_MovesTheActiveDeviceFromTheScriptsPadPress()
    {
        List<(InputDevice Device, bool Changed, PadFamily Family)> steps = Driven(new InputScript().Wait(1).Tap(PadButton.South).Wait(1).Build());

        Assert.Equal([InputDevice.KeyboardMouse, InputDevice.Gamepad, InputDevice.Gamepad], steps.Select(static step => step.Device));
        Assert.Equal([false, true, false], steps.Select(static step => step.Changed));
    }

    // A driver records the pad's family as it records window focus, and a step reads it back. A change
    // of family alone is no use of the pad.
    [Fact]
    public void ADriversPadFamily_ReachesTheStepWithoutMovingTheActiveDevice()
    {
        DeviceSnapshot playStation = DeviceSnapshot.Empty.WithPadFamily(PadFamily.PlayStation);
        List<(InputDevice Device, bool Changed, PadFamily Family)> steps = Driven(new ScriptedInputDriver([DeviceSnapshot.Empty, playStation, playStation.With(PadButton.South)]));

        Assert.Equal([PadFamily.Generic, PadFamily.PlayStation, PadFamily.PlayStation], steps.Select(static step => step.Family));
        Assert.Equal([InputDevice.KeyboardMouse, InputDevice.KeyboardMouse, InputDevice.Gamepad], steps.Select(static step => step.Device));
    }

    // A pad found on the second step lacks its triggers, and its replacement on the fourth has them.
    // The change flag is set on each of those steps alone, and neither moves the active device.
    [Fact]
    public void ThePadsButtons_ReachTheStepAndFlagTheStepTheyChangeOn()
    {
        InputState input = new(new ActionBindings());
        DeviceSnapshot noTriggers = DeviceSnapshot.Empty.WithPadLacking(PadButton.LeftTrigger, PadButton.RightTrigger);
        List<(bool Has, bool Changed)> steps = [];

        foreach (DeviceSnapshot snapshot in (DeviceSnapshot[])[DeviceSnapshot.Empty, noTriggers, noTriggers, DeviceSnapshot.Empty])
        {
            input.Advance(snapshot);
            steps.Add((input.PadHas(PadButton.RightTrigger), input.PadButtonsChanged));
            Assert.False(input.ActiveDeviceChanged);
        }

        Assert.Equal([(true, false), (false, true), (false, false), (true, true)], steps);
    }

    // What each step of a run the driver plays reads: the active device, whether it changed and the pad's family.
    private static List<(InputDevice Device, bool Changed, PadFamily Family)> Driven(IInputDriver driver)
    {
        List<(InputDevice, bool, PadFamily)> steps = [];
        SceneFixtures.HookScene scene = new(step: (Scene _, in StepContext context) =>
            steps.Add((context.Input.ActiveDevice, context.Input.ActiveDeviceChanged, context.Input.PadFamily)));
        using SceneHost host = new(SceneTransition.ToScene(typeof(SceneFixtures.HookScene), null), (in SceneTransition _) => scene, new Run());
        FixedStepScheduler scheduler = new(0.1, 32, new InputState(new ActionBindings()), driver, host);

        while (!scheduler.Advance(0.1, DeviceSnapshot.Empty, host))
        {
        }

        return steps;
    }
}
