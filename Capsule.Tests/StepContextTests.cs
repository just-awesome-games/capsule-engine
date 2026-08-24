using Capsule.Input;

namespace Capsule.Tests;

public sealed class StepContextTests
{
    private const double SixtyHertz = 1.0 / 60.0;

    [Theory]
    [InlineData(0)]
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
