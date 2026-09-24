using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// A generator that re-reads its inputs whatever changed costs a game the whole edit loop, so the
// pipeline is held to caching an unchanged compilation and not only to the source it emits.
public sealed class GeneratorCachingTests
{
    [Fact]
    public void ASecondRunOverAnUnchangedCompilation_WalksNoReferencedAssemblyAgain()
    {
        GeneratorDriverRunResult result = GeneratorHarness.RanTwice(
            ("scenes/room.scene.json", """{"formatVersion": 6, "entities": [], "nextEntityId": 1}"""));

        // The name the generator hands WithTrackingName for its walk over every referenced
        // assembly's registry metadata.
        List<IncrementalGeneratorRunStep> runs = [];
        foreach (GeneratorRunResult generator in result.Results)
        {
            if (generator.TrackedSteps.TryGetValue("BootModel", out var tracked))
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
                    $"'BootModel' ran again for {output.Reason}.")));
    }
}
