using Capsule.Input;

namespace Capsule.Tests;

public sealed class StepContextTests
{
    [Fact]
    public void AContext_CarriesTheStepAndTheInput()
    {
        InputState input = new(new ActionBindings());

        StepContext context = new(1.0 / 60.0, input);

        Assert.Equal(1.0 / 60.0, context.DeltaSeconds);
        Assert.Same(input, context.Input);
    }
}
