using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Input;

// Turns the OS mouse into the canvas pointer, wheel and mouse buttons of a DeviceSnapshot. A button
// reads down while held and on the sample after any press, however short. An unfocused window samples
// as a still pointer with nothing held. A button held when focus returns stays unreported until it is
// released or pressed again. The first active sample reports no notch and no press.
internal sealed class MouseSampler
{
    // Canvas pixels, as last sampled while the window was active. The canvas origin until then.
    private Vector2 _pointer;

    // Buttons held when the window came back into focus, each masked until it is seen released.
    // Everything is masked until the first active sample, and a button held through the launch does not
    // click either.
    private bool _active;
    private uint _masked;

    // The snapshot bit of SDL's mouse button index, 1 to 5, and 0 for any other.
    internal static uint SdlButtonBit(int sdlButton) => sdlButton switch
    {
        1 => Bit(MouseButton.Left),
        2 => Bit(MouseButton.Middle),
        3 => Bit(MouseButton.Right),
        4 => Bit(MouseButton.X1),
        5 => Bit(MouseButton.X2),
        _ => 0,
    };

    // pixelDensity: Window pixels per window coordinate, which SDL reports the pointer in.
    internal unsafe DeviceSnapshot SampleOnto(DeviceSnapshot snapshot, in ScreenPlacement placement, float pixelDensity, bool windowActive, InputEvents events)
    {
        if (!windowActive)
        {
            return Suspend(snapshot);
        }

        // SDL's held buttons are SDL_BUTTON_MASK bits, the button index less one. The pointer is in
        // window coordinates, fractional where the OS reports finer than a pixel.
        float x;
        float y;
        uint state = Sdl3.SDL_GetMouseState(&x, &y);
        uint held = 0;
        for (int button = 1; button <= 5; button++)
        {
            if ((state & (1u << (button - 1))) != 0)
            {
                held |= SdlButtonBit(button);
            }
        }

        return Fold(
            snapshot,
            placement.ToCanvas(new Vector2(x, y) * pixelDensity),
            new Vector2(events.WheelX, events.WheelY),
            held,
            events.MouseButtonsPressed);
    }

    // The two halves of the focus rule, split from the OS read so they can be specified.
    internal DeviceSnapshot Suspend(DeviceSnapshot snapshot)
    {
        _active = false;

        return snapshot.WithPointer(_pointer);
    }

    // notches is the wheel's turn since the last sample, held the buttons down now, and pressed the
    // buttons that went down since the last sample.
    internal DeviceSnapshot Resume(
        DeviceSnapshot snapshot,
        Vector2 pointer,
        Vector2 notches,
        ReadOnlySpan<MouseButton> held,
        ReadOnlySpan<MouseButton> pressed) =>
        Fold(snapshot, pointer, notches, Down(held), Down(pressed));

    private DeviceSnapshot Fold(DeviceSnapshot snapshot, Vector2 pointer, Vector2 notches, uint held, uint pressed)
    {
        if (!_active)
        {
            _active = true;
            _masked = held;

            // Whatever the wheel and the buttons did while the window was away is where they now rest.
            notches = Vector2.Zero;
            pressed = 0;
        }

        // A masked button is released the moment the OS stops reporting it or reports it pressed again,
        // and reports normally from that press on.
        _masked &= held & ~pressed;
        _pointer = pointer;

        return snapshot.WithPointer(pointer).WithScroll(notches).WithMouseButtons((held | pressed) & ~_masked);
    }

    private static uint Down(ReadOnlySpan<MouseButton> buttons)
    {
        uint down = 0;
        foreach (MouseButton button in buttons)
        {
            down |= Bit(button);
        }

        return down;
    }

    private static uint Bit(MouseButton button) => 1u << (int)button;
}
