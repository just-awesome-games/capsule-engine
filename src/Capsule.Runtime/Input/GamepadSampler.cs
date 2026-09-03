using Capsule.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Capsule.Runtime.Input;

/// <summary>Folds the first connected gamepad into a <see cref="DeviceSnapshot"/>. The only place pad hardware enters the engine.</summary>
internal static class GamepadSampler
{
    private static readonly (PadButton Button, Buttons Xna)[] XnaMappings = BuildLookup();

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

        foreach ((PadButton button, Buttons xna) in XnaMappings)
        {
            if (pad.IsButtonDown(xna))
            {
                sampled = sampled.With(button);
            }
        }

        (float leftX, float leftY) = filter.Stick(pad.ThumbSticks.Left.X, pad.ThumbSticks.Left.Y);
        (float rightX, float rightY) = filter.Stick(pad.ThumbSticks.Right.X, pad.ThumbSticks.Right.Y);

        float leftPull = filter.Trigger(pad.Triggers.Left);
        float rightPull = filter.Trigger(pad.Triggers.Right);

        if (PadFilter.TriggerHeld(leftPull))
        {
            sampled = sampled.With(PadButton.LeftTrigger);
        }

        if (PadFilter.TriggerHeld(rightPull))
        {
            sampled = sampled.With(PadButton.RightTrigger);
        }

        return sampled
            .WithAxis(PadAxis.LeftStickX, leftX)
            .WithAxis(PadAxis.LeftStickY, leftY)
            .WithAxis(PadAxis.RightStickX, rightX)
            .WithAxis(PadAxis.RightStickY, rightY)
            .WithAxis(PadAxis.LeftTrigger, leftPull)
            .WithAxis(PadAxis.RightTrigger, rightPull);
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

    // Buttons without an XNA constant are absent from the lookup, so IsButtonDown is never
    // handed an empty flag set — which every state reports as down.
    private static (PadButton, Buttons)[] BuildLookup()
    {
        List<(PadButton, Buttons)> mappings = [];
        foreach (PadButton button in Enum.GetValues<PadButton>())
        {
            if (ToXna(button) is { } xna)
            {
                mappings.Add((button, xna));
            }
        }

        return [.. mappings];
    }

    // No discard arm: adding a PadButton without a mapping must fail the build (CS8509).
#pragma warning disable CS8524 // PadButton has no unnamed values; only a cast can produce one.
    private static Buttons? ToXna(PadButton button) => button switch
    {
        // None is never a snapshot member; the triggers are derived from the filtered pull,
        // whose deadzone is Capsule's press threshold rather than the backend's.
        PadButton.None => null,
        PadButton.LeftTrigger => null,
        PadButton.RightTrigger => null,

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
