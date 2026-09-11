using System.Numerics;
using Capsule.Input;
using Capsule.Runtime.Rendering;
using Microsoft.Xna.Framework.Input;

namespace Capsule.Runtime.Input;

// Turns the OS mouse into the pointer and the mouse buttons of a DeviceSnapshot. The window position
// it reads is mapped back through the placement the last drawn frame used, so what the simulation sees
// is a canvas position whatever the window's size is and wherever the fit put the layer.
internal static class MouseSampler
{
    internal static DeviceSnapshot SampleOnto(DeviceSnapshot snapshot, in ScreenPlacement placement)
    {
        MouseState mouse = Mouse.GetState();

        snapshot = snapshot.WithPointer(placement.ToCanvas(new Vector2(mouse.X, mouse.Y)));
        snapshot = Held(snapshot, MouseButton.Left, mouse.LeftButton);
        snapshot = Held(snapshot, MouseButton.Right, mouse.RightButton);
        snapshot = Held(snapshot, MouseButton.Middle, mouse.MiddleButton);
        snapshot = Held(snapshot, MouseButton.X1, mouse.XButton1);

        return Held(snapshot, MouseButton.X2, mouse.XButton2);
    }

    private static DeviceSnapshot Held(in DeviceSnapshot snapshot, MouseButton button, ButtonState state) =>
        state == ButtonState.Pressed ? snapshot.With(button) : snapshot;
}
