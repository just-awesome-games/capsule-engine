using Capsule.Input;
using Capsule.Runtime.Input;
using Capsule.Runtime.Platform;

namespace Capsule.Tests.Input;

public sealed class GamepadSamplerTests
{
    // Each half of SDL's axis range scales over its own length, and either full deflection reads
    // exactly 1. A stick's Y is negated so up is positive, and a trigger clamps to [0, 1]. Every raw
    // reading converts bit for bit as it always has, which keeps recorded runs reproducing.
    [Fact]
    public void EveryRawReading_ConvertsBitForBit()
    {
        for (int raw = short.MinValue; raw <= short.MaxValue; raw++)
        {
            (float x, float y) = GamepadSampler.Stick((short)raw, (short)raw);

            Assert.Equal(Bits(ReferenceClamp(GetFromSdlAxis(raw), -1f, 1f)), Bits(x));
            Assert.Equal(Bits(ReferenceClamp(GetFromSdlAxis(raw) * -1f, -1f, 1f)), Bits(y));
            Assert.Equal(Bits(ReferenceClamp(GetFromSdlAxis(raw), 0f, 1f)), Bits(GamepadSampler.Trigger((short)raw)));
        }
    }

    // A mapping defining every SDL button and both trigger axes is a full pad. A SNES layout defines
    // neither stick click, no trigger axis and no guide button, which no PadButton names.
    [Fact]
    public void WhatTheMappingDefines_IsWhatThePadHas()
    {
        uint every = (1u << ((int)Sdl3.GamepadButton.DPadRight + 1)) - 1;
        uint snes = every & ~(1u << (int)Sdl3.GamepadButton.LeftStick) & ~(1u << (int)Sdl3.GamepadButton.RightStick)
            & ~(1u << (int)Sdl3.GamepadButton.Guide);

        Assert.Equal(0u, GamepadSampler.Lacking(every, true, true));
        Assert.Equal(
            DeviceSnapshot.Empty.WithPadLacking(PadButton.LeftTrigger, PadButton.RightTrigger, PadButton.LeftStickClick, PadButton.RightStickClick),
            DeviceSnapshot.Empty.WithPadLackingBits(GamepadSampler.Lacking(snes, false, false)));
        Assert.Equal(
            DeviceSnapshot.Empty.WithPadLacking(PadButton.East, PadButton.LeftTrigger),
            DeviceSnapshot.Empty.WithPadLackingBits(GamepadSampler.Lacking(every & ~(1u << (int)Sdl3.GamepadButton.East), false, true)));
    }

    private static float GetFromSdlAxis(int axis) => axis < 0 ? axis / 32768f : axis / 32767f;

    private static float ReferenceClamp(float value, float min, float max)
    {
        value = value > max ? max : value;
        value = value < min ? min : value;

        return value;
    }

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
}
