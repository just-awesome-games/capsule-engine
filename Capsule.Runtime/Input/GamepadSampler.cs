using Capsule.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Capsule.Runtime.Input;

/// <summary>Folds the first connected gamepad into a <see cref="DeviceSnapshot"/>. The only place pad hardware enters the engine.</summary>
internal static class GamepadSampler
{
    private static readonly Buttons[] XnaByPadButton = BuildLookup();

    /// <summary>
    /// <paramref name="snapshot"/> with this frame's pad buttons additionally held and its
    /// axes set through <paramref name="filter"/>. With no pad connected the snapshot is
    /// returned untouched, which is the same value a connected pad at rest produces.
    /// </summary>
    internal static DeviceSnapshot SampleOnto(in DeviceSnapshot snapshot, PadFilter filter)
    {
        GamePadState pad = FirstConnected();
        if (!pad.IsConnected)
        {
            return snapshot;
        }

        DeviceSnapshot sampled = snapshot;

        // From 1: index 0 is PadButton.None, which is never a snapshot member.
        for (int index = 1; index < XnaByPadButton.Length; index++)
        {
            if (pad.IsButtonDown(XnaByPadButton[index]))
            {
                sampled = sampled.With((PadButton)index);
            }
        }

        (float leftX, float leftY) = filter.Stick(pad.ThumbSticks.Left.X, pad.ThumbSticks.Left.Y);
        (float rightX, float rightY) = filter.Stick(pad.ThumbSticks.Right.X, pad.ThumbSticks.Right.Y);

        return sampled
            .WithAxis(PadAxis.LeftStickX, leftX)
            .WithAxis(PadAxis.LeftStickY, leftY)
            .WithAxis(PadAxis.RightStickX, rightX)
            .WithAxis(PadAxis.RightStickY, rightY)
            .WithAxis(PadAxis.LeftTrigger, filter.Trigger(pad.Triggers.Left))
            .WithAxis(PadAxis.RightTrigger, filter.Trigger(pad.Triggers.Right));
    }

    // GamePadDeadZone.None: the backend's own filtering would apply a second, differently
    // shaped deadzone under the one PadFilter applies.
    private static GamePadState FirstConnected()
    {
        for (PlayerIndex player = PlayerIndex.One; player <= PlayerIndex.Four; player++)
        {
            GamePadState state = GamePad.GetState(player, GamePadDeadZone.None);
            if (state.IsConnected)
            {
                return state;
            }
        }

        return default;
    }

    private static Buttons[] BuildLookup()
    {
        PadButton[] buttons = Enum.GetValues<PadButton>();

        int length = 0;
        foreach (PadButton button in buttons)
        {
            length = Math.Max(length, (int)button + 1);
        }

        Buttons[] lookup = new Buttons[length];
        foreach (PadButton button in buttons)
        {
            lookup[(int)button] = ToXna(button);
        }

        return lookup;
    }

    // No discard arm: adding a PadButton without a mapping must fail the build (CS8509).
#pragma warning disable CS8524 // PadButton has no unnamed values; only a cast can produce one.
    private static Buttons ToXna(PadButton button) => button switch
    {
        // Buttons is a flag set with no zero member, so nothing maps to None.
        PadButton.None => default,

        PadButton.DPadUp => Buttons.DPadUp,
        PadButton.DPadDown => Buttons.DPadDown,
        PadButton.DPadLeft => Buttons.DPadLeft,
        PadButton.DPadRight => Buttons.DPadRight,

        PadButton.South => Buttons.A,
        PadButton.East => Buttons.B,
        PadButton.West => Buttons.X,
        PadButton.North => Buttons.Y,

        PadButton.LeftShoulder => Buttons.LeftShoulder,
        PadButton.RightShoulder => Buttons.RightShoulder,

        PadButton.LeftStickClick => Buttons.LeftStick,
        PadButton.RightStickClick => Buttons.RightStick,

        PadButton.Start => Buttons.Start,
        PadButton.Select => Buttons.Back,
    };
#pragma warning restore CS8524
}
