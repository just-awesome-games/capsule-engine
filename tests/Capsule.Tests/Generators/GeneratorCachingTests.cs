using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// A generator that re-reads its inputs whatever changed costs a game the whole edit loop, so the
// pipeline is held to caching an unchanged compilation and not only to the source it emits.
public sealed class GeneratorCachingTests
{
    private const string Font = """
        info face="Test" size=12
        common lineHeight=15 base=12 scaleW=32 scaleH=32 pages=1
        page id=0 file="menu.png"
        chars count=1
        char id=65 x=0 y=0 width=4 height=6 xoffset=0 yoffset=0 xadvance=5 page=0 chnl=15
        """;

    private const string Sheet = """
        { "formatVersion": 1, "texture": "hero.png",
          "frames": [ { "name": "idle", "x": 0, "y": 0, "width": 8, "height": 8 } ] }
        """;

    [Fact]
    public void ASecondRunOverAnUnchangedCompilation_ParsesNothingAgain()
    {
        GeneratorDriverRunResult result = GeneratorHarness.RanTwice(
            ("fonts/menu.fnt", Font),
            ("fonts/menu.png", null),
            ("sprites/hero.sheet.json", Sheet),
            ("textures/hero.png", null));

        // The names the generators hand WithTrackingName: the '.fnt' read, the sheet read, and the
        // walk over every referenced assembly's registry metadata.
        AssertCached(result, "FontParse");
        AssertCached(result, "SheetParse");
        AssertCached(result, "BootModel");
    }

    private static void AssertCached(GeneratorDriverRunResult result, string step)
    {
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
