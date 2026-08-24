using Capsule.Input;

namespace Capsule.Tests;

public sealed class StepContextTests
{
    private const double SixtyHertz = 1.0 / 60.0;

    [Fact]
    public void AContext_CarriesTheStepAndTheInput()
    {
        InputState input = new(new ActionBindings());

        StepContext context = new(SixtyHertz, input, 0);

        Assert.Equal(SixtyHertz, context.DeltaSeconds);
        Assert.Same(input, context.Input);
    }

    [Fact]
    public void TheFirstStep_IsTickZeroAtTimeZero()
    {
        StepContext context = new(SixtyHertz, new InputState(new ActionBindings()), 0);

        Assert.Equal(0, context.Tick);
        Assert.Equal(0.0, context.TotalSeconds);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(216_000)]
    public void SimulatedTime_IsTheTickTimesTheStep(long tick)
    {
        StepContext context = new(SixtyHertz, new InputState(new ActionBindings()), tick);

        Assert.Equal(tick, context.Tick);
        Assert.Equal(tick * SixtyHertz, context.TotalSeconds);
    }
}
