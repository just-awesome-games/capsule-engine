using Capsule.Runtime.Input;

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

    private static float GetFromSdlAxis(int axis) => axis < 0 ? axis / 32768f : axis / 32767f;

    private static float ReferenceClamp(float value, float min, float max)
    {
        value = value > max ? max : value;
        value = value < min ? min : value;

        return value;
    }

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
}
