using Capsule.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Capsule.Runtime.Input;

// Folds the first connected gamepad into a DeviceSnapshot. The only place pad hardware enters the
// engine. One instance per host, remembering which player index answered last.
internal sealed class GamepadSampler
{
    // How many samples pass between sweeps for a pad on another player index while none is connected.
    // Each index costs a backend call, and a run played on the keyboard would otherwise pay four of
    // them every frame for pads it never finds.
    private const int SweepInterval = 30;

    private static readonly (PadButton Button, Buttons Xna)[] XnaMappings = BuildLookup();

    // The player index the last connected pad was found on, tried first every sample.
    private PlayerIndex _connectedPlayer = PlayerIndex.One;
    private int _sweepCountdown;

    // snapshot with this frame's pad buttons also held and its axes set through filter. With no pad
    // connected it is returned untouched.
    internal DeviceSnapshot SampleOnto(in DeviceSnapshot snapshot, PadFilter filter)
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

    // GamePadDeadZone.None, because the backend's own filtering would apply a second, differently
    // shaped deadzone under PadFilter's.
    private GamePadState FirstConnected()
    {
        GamePadState remembered = GamePad.GetState(_connectedPlayer, GamePadDeadZone.None);
        if (remembered.IsConnected)
        {
            return remembered;
        }

        if (--_sweepCountdown > 0)
        {
            return default;
        }

        _sweepCountdown = SweepInterval;

        for (PlayerIndex player = PlayerIndex.One; player <= PlayerIndex.Four; player++)
        {
            if (player == _connectedPlayer)
            {
                continue;
            }

            GamePadState state = GamePad.GetState(player, GamePadDeadZone.None);
            if (state.IsConnected)
            {
                _connectedPlayer = player;

                return state;
            }
        }

        return default;
    }

    // Buttons without an XNA constant are left out of the lookup, so IsButtonDown is never handed an
    // empty flag set, which every state reports as down.
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
        // None is not a snapshot member, and the triggers derive from the filtered pull.
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
