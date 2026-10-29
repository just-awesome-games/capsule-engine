using Capsule.Tests.Localization;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class RunTextAllocationTests
{
    // Once its buffers are warm, a call allocates what a string of the same length costs. A string argument
    // keeps the count exact in this Debug build, which boxes a formatted value type that optimized code does not.
    [Fact]
    public void GetText_AllocatesOnlyTheStringItReturns()
    {
        Run run = new() { Language = "de" };
        int length = run.GetText(StringTableFixtures.Pair, StringTableFixtures.On, "Avatar").Length;

        long text = StepMeasurement.Bytes(_ => run.GetText(StringTableFixtures.Pair, StringTableFixtures.On, "Avatar"), warmup: 100, measured: 400);
        long strings = StepMeasurement.Bytes(_ => new string(' ', length), warmup: 100, measured: 400);

        Assert.Equal(strings, text);
    }
}
