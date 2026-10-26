using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Input;
using Capsule.Runtime.Sdl;

namespace Capsule.Tests.Input;

// The feed's record of SDL3's events, handed to it as the host's event callback does. Only the events
// subsystem runs. The keyboard and the mouse SDL reads hold nothing down.
[Collection(SdlEventsCollection.Name)]
public sealed class InputFeedTests : IDisposable
{
    private readonly InputFeed _feed;

    public InputFeedTests()
    {
        Assert.True(Sdl3.SDL_InitSubSystem(Sdl3.InitEvents), Sdl3.Error());
        _feed = new InputFeed();
    }

    public void Dispose()
    {
        Sdl3.SDL_QuitSubSystem(Sdl3.InitEvents);
    }

    // The held state a sample reads has already let go of the key. The press alone carries it.
    [Fact]
    public void AKeyPressedAndReleasedBetweenSamples_IsDownForOneSample()
    {
        Push(Keyboard(Sdl3.EventKeyDown, Scancode.Space, ' '));
        Push(Keyboard(Sdl3.EventKeyUp, Scancode.Space, ' '));

        Assert.True(KeyboardSampler.Sample(_feed.Take()).IsDown(Key.Space));
        Assert.False(KeyboardSampler.Sample(_feed.Take()).IsDown(Key.Space));
    }

    // On an AZERTY layout the key in W's position prints Z.
    [Fact]
    public void AKey_IsReadByItsPositionNotItsCharacter()
    {
        Push(Keyboard(Sdl3.EventKeyDown, Scancode.W, 'z'));

        DeviceSnapshot sampled = KeyboardSampler.Sample(_feed.Take());

        Assert.True(sampled.IsDown(Key.W));
        Assert.False(sampled.IsDown(Key.Z));
    }

    // A layout change relabels the keys, and the host rereads the layout from the sample after it.
    [Fact]
    public void AKeymapChange_IsRecordedForOneSample()
    {
        Push(new Sdl3.GamepadDeviceEvent { Type = Sdl3.EventKeymapChanged });

        Assert.True(_feed.Take().KeymapChanged);
        Assert.False(_feed.Take().KeymapChanged);
    }

    // A wheel finer than a notch reports fractions, and SDL's whole notches are what reach the sample.
    [Fact]
    public void WheelNotchesAndMouseClicks_ReachTheSampleAfterThem()
    {
        MouseSampler sampler = new();
        ScreenPlacement placement = new(Vector2.Zero, 1f);
        sampler.SampleOnto(DeviceSnapshot.Empty, placement, 1f, windowActive: true, _feed.Take());

        Push(new Sdl3.MouseButtonEvent { Type = Sdl3.EventMouseButtonDown, Button = 3, Down = 1, Clicks = 1 });
        Push(new Sdl3.MouseWheelEvent { Type = Sdl3.EventMouseWheel, X = 0.5f, Y = 2.4f, IntegerX = 0, IntegerY = 2 });
        Push(new Sdl3.MouseWheelEvent { Type = Sdl3.EventMouseWheel, X = 1f, Y = -1f, IntegerX = 1, IntegerY = -1 });

        // Natural scrolling reports both values reversed and says so. Read back, it turns as the others do.
        Push(new Sdl3.MouseWheelEvent { Type = Sdl3.EventMouseWheel, Direction = Sdl3.MouseWheelFlipped, X = -2f, Y = -3f, IntegerX = -2, IntegerY = -3 });

        DeviceSnapshot sampled = sampler.SampleOnto(DeviceSnapshot.Empty, placement, 1f, windowActive: true, _feed.Take());

        Assert.True(sampled.IsDown(MouseButton.Right));
        Assert.False(sampled.IsDown(MouseButton.Middle));
        Assert.Equal(new Vector2(3f, 4f), sampled.Scroll);
    }

    // A trigger is an axis to SDL. Pulled past the deadzone and let go before the sample, it is still
    // that pad's press, and the press makes the pad active as a button's does.
    [Fact]
    public void ATriggerPulledAndReleasedBetweenSamples_IsAPressThatMakesItsPadActive()
    {
        const uint First = 3;
        const uint Second = 9;
        _feed.UseTriggerDeadzone(0.4f);
        PadRoster pads = new();
        pads.Sync([First, Second], static id => new Pad(id, nint.Zero, "pad", PadFamily.Generic), static _ => { });

        // Below the deadzone moves no button. Past it and back is one press.
        Push(TriggerAxis(Second, Sdl3.GamepadAxis.RightTrigger, 13000));
        Push(TriggerAxis(Second, Sdl3.GamepadAxis.RightTrigger, 20000));
        Push(TriggerAxis(Second, Sdl3.GamepadAxis.RightTrigger, 25000));
        Push(TriggerAxis(Second, Sdl3.GamepadAxis.RightTrigger, 0));
        InputEvents events = _feed.Take();
        pads.Use(events.LastPadPressed);

        DeviceSnapshot sampled = GamepadSampler.WithPresses(DeviceSnapshot.Empty, events.PadButtonsPressed(Second));
        Assert.True(sampled.IsDown(PadButton.RightTrigger));
        Assert.False(sampled.IsDown(PadButton.LeftTrigger));
        Assert.Equal(Second, pads.ActiveId);
        Assert.Equal(0u, _feed.Take().PadButtonsPressed(Second));
    }

    private static Sdl3.GamepadAxisEvent TriggerAxis(uint pad, Sdl3.GamepadAxis axis, short value) =>
        new() { Type = Sdl3.EventGamepadAxisMotion, Which = pad, Axis = (byte)axis, Value = value };

    private static Sdl3.KeyboardEvent Keyboard(uint type, Scancode scancode, char character) => new()
    {
        Type = type,
        Scancode = (int)scancode,
        Key = character,
        Down = type == Sdl3.EventKeyDown ? (byte)1 : (byte)0,
    };

    // SDL copies a whole SDL_Event from the pointer, whatever the type's own size.
    private void Push<T>(T sdlEvent)
        where T : struct
    {
        nint buffer = Marshal.AllocHGlobal(Sdl3.EventSize);
        try
        {
            Marshal.Copy(new byte[Sdl3.EventSize], 0, buffer, Sdl3.EventSize);
            Marshal.StructureToPtr(sdlEvent, buffer, fDeleteOld: false);
            _feed.Record(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
