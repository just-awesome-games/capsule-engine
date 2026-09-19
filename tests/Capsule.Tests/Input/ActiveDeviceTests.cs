using System.Numerics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;

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
        Recording scene = new();
        using SceneHost host = new(SceneTransition.ToScene(typeof(Recording), null), (in SceneTransition _) => scene, new Run());
        IInputDriver driver = new InputScript().Wait(1).Tap(PadButton.South).Wait(1).Build();
        FixedStepScheduler scheduler = new(0.1, 32, new ActionBindings(), driver, host);

        while (!scheduler.Advance(0.1, DeviceSnapshot.Empty, host))
        {
        }

        Assert.Equal(
            [InputDevice.KeyboardMouse, InputDevice.Gamepad, InputDevice.Gamepad],
            scene.Devices);
        Assert.Equal([false, true, false], scene.Changes);
    }

    private sealed class Recording : Scene
    {
        internal List<InputDevice> Devices { get; } = [];

        internal List<bool> Changes { get; } = [];

        protected override void OnStep(in StepContext context)
        {
            Devices.Add(context.Input.ActiveDevice);
            Changes.Add(context.Input.ActiveDeviceChanged);
        }
    }
}
