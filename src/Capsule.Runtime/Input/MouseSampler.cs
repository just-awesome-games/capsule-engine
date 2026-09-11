using System.Numerics;
using Capsule.Input;
using Capsule.Runtime.Rendering;
using Microsoft.Xna.Framework.Input;

namespace Capsule.Runtime.Input;

// Turns the OS mouse into the pointer and the mouse buttons of a DeviceSnapshot. The window position
// it reads is mapped back through the screen layer's placement, so what the simulation sees is a
// canvas position whatever the window's size is and wherever the fit put the layer. The OS reports
// the mouse whether or not the window has focus, so an unfocused window is sampled as a pointer
// standing still with nothing held — one host's window never moves another's menu focus — and a
// button still held when focus returns stays unreported until it is released, so the return itself
// is never a click.
internal sealed class MouseSampler
{
    private static readonly MouseButton[] Buttons =
        [MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.X1, MouseButton.X2];

    // Canvas pixels, as last sampled while the window was active; the canvas origin until one was.
    private Vector2 _pointer;

    // Buttons held when the window came back into focus, each masked until it is seen released.
    // Everything is masked until the first active sample, so a button held through the launch
    // does not click either.
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

        return Resume(snapshot, placement.ToCanvas(new Vector2(mouse.X, mouse.Y)), held);
    }

    // The two halves of the focus rule, split from the OS read so they can be specified.
    internal DeviceSnapshot Suspend(DeviceSnapshot snapshot)
    {
        _active = false;

        return snapshot.WithPointer(_pointer);
    }

    internal DeviceSnapshot Resume(DeviceSnapshot snapshot, Vector2 pointer, params ReadOnlySpan<MouseButton> held)
    {
        uint down = 0;
        foreach (MouseButton button in held)
        {
            down |= Bit(button);
        }

        return Resume(snapshot, pointer, down);
    }

    private DeviceSnapshot Resume(DeviceSnapshot snapshot, Vector2 pointer, uint held)
    {
        if (!_active)
        {
            _active = true;
            _masked = held;
        }

        // A masked button is released the moment the OS stops reporting it, and reports normally
        // from its next press on.
        _masked &= held;
        _pointer = pointer;

        snapshot = snapshot.WithPointer(pointer);

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

    private static uint Bit(MouseButton button, ButtonState state) =>
        state == ButtonState.Pressed ? Bit(button) : 0;

    private static uint Bit(MouseButton button) => 1u << (int)button;
}
