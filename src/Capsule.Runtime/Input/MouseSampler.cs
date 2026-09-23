using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Microsoft.Xna.Framework.Input;

namespace Capsule.Runtime.Input;

// Turns the OS mouse into the pointer and the mouse buttons of a DeviceSnapshot. The window position
// it reads is mapped back through the screen layer's placement, so the simulation sees a canvas
// position whatever the window's size and wherever the fit put the layer.
//
// The OS reports the mouse whether or not the window has focus. An unfocused window is sampled as a
// pointer standing still with nothing held, and one host's window never moves another's menu focus. A
// button still held when focus returns stays unreported until it is released, so the return is not a
// click. The wheel behaves the same way: nothing while unfocused, and the cumulative baseline is
// re-seeded on the first active sample, and a wheel turned away from the window does not arrive as one
// enormous notch.
internal sealed class MouseSampler
{
    // MonoGame's SDL platform adds 120 per notch to each cumulative wheel value, positive away from
    // the user and positive to the right, which is the snapshot's own convention.
    private const float NotchUnit = 120f;

    private static readonly MouseButton[] Buttons =
        [MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.X1, MouseButton.X2];

    // Canvas pixels, as last sampled while the window was active. The canvas origin until then.
    private Vector2 _pointer;

    // MonoGame's cumulative wheel values as last sampled while active. Differences of these are the
    // notches a step sees.
    private int _wheelHorizontal;
    private int _wheelVertical;

    // Buttons held when the window came back into focus, each masked until it is seen released.
    // Everything is masked until the first active sample, and a button held through the launch does not
    // click either.
    private bool _active;
    private uint _masked;

    internal DeviceSnapshot SampleOnto(DeviceSnapshot snapshot, in ScreenPlacement placement, bool windowActive)
    {
        if (!windowActive)
        {
            return Suspend(snapshot);
        }

        MouseState mouse = Mouse.GetState();

        uint held = 0;
        held |= Bit(MouseButton.Left, mouse.LeftButton);
        held |= Bit(MouseButton.Right, mouse.RightButton);
        held |= Bit(MouseButton.Middle, mouse.MiddleButton);
        held |= Bit(MouseButton.X1, mouse.XButton1);
        held |= Bit(MouseButton.X2, mouse.XButton2);

        return Resume(
            snapshot,
            placement.ToCanvas(new Vector2(mouse.X, mouse.Y)),
            mouse.HorizontalScrollWheelValue,
            mouse.ScrollWheelValue,
            held);
    }

    // The two halves of the focus rule, split from the OS read so they can be specified.
    internal DeviceSnapshot Suspend(DeviceSnapshot snapshot)
    {
        _active = false;

        return snapshot.WithPointer(_pointer);
    }

    // horizontal and vertical are the OS wheel's cumulative values. Repeating the last pair turns the
    // wheel not at all.
    internal DeviceSnapshot Resume(
        DeviceSnapshot snapshot,
        Vector2 pointer,
        int horizontal,
        int vertical,
        params ReadOnlySpan<MouseButton> buttons) =>
        Resume(snapshot, pointer, horizontal, vertical, Down(buttons));

    private DeviceSnapshot Resume(DeviceSnapshot snapshot, Vector2 pointer, int horizontal, int vertical, uint held)
    {
        if (!_active)
        {
            _active = true;
            _masked = held;

            // Whatever the wheel did while the window was away is where it now rests, not a notch.
            _wheelHorizontal = horizontal;
            _wheelVertical = vertical;
        }

        // A masked button is released the moment the OS stops reporting it, and reports normally
        // from its next press on.
        _masked &= held;
        _pointer = pointer;

        Vector2 notches = new((horizontal - _wheelHorizontal) / NotchUnit, (vertical - _wheelVertical) / NotchUnit);
        _wheelHorizontal = horizontal;
        _wheelVertical = vertical;

        snapshot = snapshot.WithPointer(pointer).WithScroll(notches);

        uint reported = held & ~_masked;
        foreach (MouseButton button in Buttons)
        {
            if ((reported & Bit(button)) != 0)
            {
                snapshot = snapshot.With(button);
            }
        }

        return snapshot;
    }

    private static uint Down(ReadOnlySpan<MouseButton> held)
    {
        uint down = 0;
        foreach (MouseButton button in held)
        {
            down |= Bit(button);
        }

        return down;
    }

    private static uint Bit(MouseButton button, ButtonState state) =>
        state == ButtonState.Pressed ? Bit(button) : 0;

    private static uint Bit(MouseButton button) => 1u << (int)button;
}
