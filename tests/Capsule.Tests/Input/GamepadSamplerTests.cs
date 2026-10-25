using Capsule.Runtime.Input;

namespace Capsule.Tests.Input;

public sealed class GamepadSamplerTests
{
    // A stick and a trigger read exactly as they did when the graphics substrate read the pad, for
    // every raw reading SDL can report. The reference is GamePad.PlatformGetState in MonoGame
    // DesktopGL 3.8.5.1, transcribed: GetFromSdlAxis scales, the thumbsticks negate Y and clamp to
    // [-1, 1], and the triggers clamp to [0, 1] through MathHelper.Clamp.
    [Fact]
    public void EveryRawReading_ConvertsBitForBitAsTheSubstrateDid()
    {
        for (int raw = short.MinValue; raw <= short.MaxValue; raw++)
        {
            (float x, float y) = GamepadSampler.Stick((short)raw, (short)raw);

            Assert.Equal(Bits(ReferenceClamp(GetFromSdlAxis(raw), -1f, 1f)), Bits(x));
            Assert.Equal(Bits(ReferenceClamp(GetFromSdlAxis(raw) * -1f, -1f, 1f)), Bits(y));
            Assert.Equal(Bits(ReferenceClamp(GetFromSdlAxis(raw), 0f, 1f)), Bits(GamepadSampler.Trigger((short)raw)));
        }
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
