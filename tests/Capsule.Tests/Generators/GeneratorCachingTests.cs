using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// A generator that re-reads its inputs whatever changed costs a game the whole edit loop, so the
// pipeline is held to caching an unchanged compilation and not only to the source it emits.
public sealed class GeneratorCachingTests
{
    // The names the generator hands WithTrackingName: its walk over every referenced assembly's
    // registry metadata, and each plan a generated file is rendered from. A plan holding a
    // diagnostic is rebuilt on every run, so this input faults nothing.
    [Theory]
    [InlineData("BootModel")]
    [InlineData("EntityPlan")]
    [InlineData("ScenePlan")]
    [InlineData("InputDriverPlan")]
    [InlineData("BootPlan")]
    public void ASecondRunOverAnUnchangedCompilation_RunsTheStepAgainForNothing(string step)
    {
        GeneratorDriverRunResult result = GeneratorHarness.RanTwice(
            ("scenes/room.scene.json", """{"formatVersion": 7, "entities": [], "nextEntityId": 1}"""));

        List<IncrementalGeneratorRunStep> runs = [];
        foreach (GeneratorRunResult generator in result.Results)
        {
            if (generator.TrackedSteps.TryGetValue(step, out var tracked))
            {
                runs.AddRange(tracked);
            }
        }

        Assert.NotEmpty(runs);
        Assert.All(
            runs,
            run => Assert.All(
                run.Outputs,
                output => Assert.True(
                    output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"'{step}' ran again for {output.Reason}.")));
    }
}
