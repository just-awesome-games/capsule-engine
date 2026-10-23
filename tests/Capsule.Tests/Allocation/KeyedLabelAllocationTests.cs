using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Localization;
using Capsule.Tests.Rendering;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class KeyedLabelAllocationTests
{
    // Every step switches the language, and each draw resolves the texts again with their kept arguments,
    // selecting the plural form anew. The buffers grown on the first passes serve the rest.
    [Fact]
    public void AKeyedLabel_ResolvesAndReResolvesAllocatingNothingOnceWarm()
    {
        Label label = new(FontFixtures.Font());
        label.SetText(StringTableFixtures.Score, 12345);
        Label plural = new(FontFixtures.Font());
        plural.SetText(StringTableFixtures.Bolts, 22L);
        Scene scene = new();
        scene.Add(new SceneFixtures.Holder(Vector2.Zero, label));
        scene.Add(new SceneFixtures.Holder(Vector2.Zero, plural));
        scene.Add(new Switcher());
        using SceneSimulation simulation = new(scene);

        StepSample[] samples = StepMeasurement.Measure(simulation, warmupSteps: 100, measuredSteps: 400);

        Assert.All(samples, sample => Assert.Equal(0, sample.ViewBytes));
        Assert.All(samples, sample => Assert.Equal(0, sample.StepBytes));
    }

    private sealed class Switcher() : Entity(Vector2.Zero)
    {
        private int _step;

        protected internal override void OnStep(in StepContext context)
        {
            _step++;
            Run.Language = (_step % 3) switch
            {
                0 => "de",
                1 => "pl",
                _ => "pt-BR",
            };
        }
    }
}
