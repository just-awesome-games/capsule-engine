using Capsule.Input;
using Capsule.Runtime.Input;

namespace Capsule.Tests.Runtime;

public sealed class PadFilterTests
{
    private const float Tolerance = 1e-5f;

    private const float ConfiguredStickDeadzone = 0.5f;
    private const float ConfiguredTriggerDeadzone = 0.4f;

    private static readonly PadFilter Default =
        new(InputConfiguration.DefaultStickDeadzone, InputConfiguration.DefaultTriggerDeadzone);

    // A stick's whole response on one axis: dead to the deadzone, barely off centre on the first
    // reading past it, half at halfway to the edge, and one at full deflection — whatever radius the
    // host was configured with. The other axis never leaves centre.
    [Theory]
    [InlineData(InputConfiguration.DefaultStickDeadzone, 0f, 0f)]
    [InlineData(InputConfiguration.DefaultStickDeadzone, 0.17f, 0f)]
    [InlineData(InputConfiguration.DefaultStickDeadzone, InputConfiguration.DefaultStickDeadzone, 0f)]
    [InlineData(
        InputConfiguration.DefaultStickDeadzone,
        InputConfiguration.DefaultStickDeadzone + 0.001f,
        0.001f / (1f - InputConfiguration.DefaultStickDeadzone))]
    [InlineData(
        InputConfiguration.DefaultStickDeadzone,
        InputConfiguration.DefaultStickDeadzone + ((1f - InputConfiguration.DefaultStickDeadzone) / 2f),
        0.5f)]
    [InlineData(ConfiguredStickDeadzone, ConfiguredStickDeadzone + ((1f - ConfiguredStickDeadzone) / 2f), 0.5f)]
    [InlineData(InputConfiguration.DefaultStickDeadzone, 1f, 1f)]
    public void AStick_IsDeadToItsDeadzoneAndRemappedPastIt(float deadzone, float raw, float expected)
    {
        (float x, float y) = new PadFilter(deadzone, InputConfiguration.DefaultTriggerDeadzone).Stick(raw, 0f);

        Assert.Equal(expected, x, Tolerance);
        Assert.Equal(0f, y, Tolerance);
    }

    [Fact]
    public void FilteringPreservesDirection()
    {
        const float RawX = 0.9f;
        const float RawY = -0.45f;

        (float x, float y) = Default.Stick(RawX, RawY);

        Assert.Equal(RawY / RawX, y / x, Tolerance);
        Assert.True(x > 0f);
        Assert.True(y < 0f);
    }

    [Fact]
    public void EveryFilteredStick_StaysInsideTheUnitDisk()
    {
        for (int degrees = 0; degrees < 360; degrees += 7)
        {
            float radians = degrees * MathF.PI / 180f;

            (float x, float y) = Default.Stick(1.4f * MathF.Cos(radians), 1.4f * MathF.Sin(radians));

            Assert.InRange(MathF.Sqrt((x * x) + (y * y)), 0f, 1f + Tolerance);
        }
    }

    // A trigger's whole pull: dead to the deadzone, remapped past it, clamped at the top.
    [Theory]
    [InlineData(InputConfiguration.DefaultTriggerDeadzone, 0f, 0f)]
    [InlineData(InputConfiguration.DefaultTriggerDeadzone, InputConfiguration.DefaultTriggerDeadzone, 0f)]
    [InlineData(
        InputConfiguration.DefaultTriggerDeadzone,
        InputConfiguration.DefaultTriggerDeadzone + ((1f - InputConfiguration.DefaultTriggerDeadzone) / 2f),
        0.5f)]
    [InlineData(ConfiguredTriggerDeadzone, ConfiguredTriggerDeadzone + ((1f - ConfiguredTriggerDeadzone) / 2f), 0.5f)]
    [InlineData(InputConfiguration.DefaultTriggerDeadzone, 1.2f, 1f)]
    public void ATrigger_IsDeadToItsDeadzoneAndRemappedPastIt(float deadzone, float raw, float expected) =>
        Assert.Equal(expected, new PadFilter(InputConfiguration.DefaultStickDeadzone, deadzone).Trigger(raw), Tolerance);

    [Theory]
    [InlineData(0f, false)]
    [InlineData(InputConfiguration.DefaultTriggerDeadzone, false)]
    [InlineData(InputConfiguration.DefaultTriggerDeadzone + 0.001f, true)]
    [InlineData(1f, true)]
    public void TheTriggerButton_IsHeldExactlyPastTheDeadzone(float raw, bool held)
    {
        Assert.Equal(held, PadFilter.TriggerHeld(Default.Trigger(raw)));
    }
}
