using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// A generator that re-reads its inputs whatever changed costs a game the whole edit loop. Every model the
// pipeline carries compares by value, so an edit that changes nothing a model holds leaves each plan cached.
public sealed class GeneratorCachingTests
{
    private const string Before = $$"""
        {{GeneratorHarness.Preamble}}

        public sealed class Door(EntitySpawn spawn) : Entity(spawn)
        {
            [Authorable]
            public int Lock { get; set; }

            private static int Count() => 1;
        }

        public sealed class Hall(SceneContent content) : Scene(content);
        """;

    // The names the generator hands WithTrackingName. A plan holding a diagnostic is rebuilt on every run,
    // so this input faults nothing.
    [Theory]
    [InlineData("BootModel")]
    [InlineData("EntityPlan")]
    [InlineData("ScenePlan")]
    [InlineData("InputDriverPlan")]
    [InlineData("BootPlan")]
    public void AnEditToAMethodBody_RunsNoPlanAgain(string step)
    {
        GeneratorDriverRunResult result = GeneratorHarness.RanTwice(
            Before,
            Before.Replace("=> 1;", "=> 2;", StringComparison.Ordinal),
            ("hall.scene.json", """{"entities": []}"""));

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
