using Capsule.Runtime.Input;

namespace Capsule.Tests.Runtime;

public sealed class PadFilterTests
{
    private const float Tolerance = 1e-5f;

    private const float ConfiguredStickDeadzone = 0.5f;
    private const float ConfiguredTriggerDeadzone = 0.4f;

    private static readonly PadFilter Default =
        new(PadFilter.DefaultStickDeadzone, PadFilter.DefaultTriggerDeadzone);

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.17f, 0.17f)]
    [InlineData(PadFilter.DefaultStickDeadzone, 0f)]
    public void AStickInsideTheDeadzone_ReadsCentred(float x, float y)
    {
        (float filteredX, float filteredY) = Default.Stick(x, y);

        Assert.Equal(0f, filteredX);
        Assert.Equal(0f, filteredY);
    }

    [Fact]
    public void AStickJustOutsideTheDeadzone_ReadsNearZero()
    {
        (float x, float y) = Default.Stick(PadFilter.DefaultStickDeadzone + 0.001f, 0f);

        Assert.InRange(x, 0f, 0.01f);
        Assert.Equal(0f, y);
    }

    [Fact]
    public void AFullyDeflectedStick_ReadsOne()
    {
        (float x, float y) = Default.Stick(1f, 0f);

        Assert.Equal(1f, x, Tolerance);
        Assert.Equal(0f, y, Tolerance);
    }

    [Theory]
    [InlineData(PadFilter.DefaultStickDeadzone)]
    [InlineData(ConfiguredStickDeadzone)]
    public void TheRemap_SpansTheDeadzoneToOne(float deadzone)
    {
        PadFilter filter = new(deadzone, PadFilter.DefaultTriggerDeadzone);

        float halfway = deadzone + ((1f - deadzone) / 2f);

        (float x, _) = filter.Stick(halfway, 0f);

        Assert.Equal(0.5f, x, Tolerance);
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

    [Theory]
    [InlineData(0f)]
    [InlineData(PadFilter.DefaultTriggerDeadzone)]
    public void ATriggerInsideTheDeadzone_ReadsReleased(float raw)
    {
        Assert.Equal(0f, Default.Trigger(raw));
    }

    [Theory]
    [InlineData(PadFilter.DefaultTriggerDeadzone)]
    [InlineData(ConfiguredTriggerDeadzone)]
    public void ATriggerRemapsFromTheDeadzoneToOne(float deadzone)
    {
        PadFilter filter = new(PadFilter.DefaultStickDeadzone, deadzone);

        float halfway = deadzone + ((1f - deadzone) / 2f);

        Assert.Equal(0.5f, filter.Trigger(halfway), Tolerance);
    }

    [Fact]
    public void ATriggerPastTheEndOfItsRange_Clamps()
    {
        Assert.Equal(1f, Default.Trigger(1.2f), Tolerance);
    }
}
