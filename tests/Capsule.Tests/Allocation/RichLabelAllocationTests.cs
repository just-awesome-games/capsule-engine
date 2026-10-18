using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Rendering;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class RichLabelAllocationTests
{
    // One label draws unchanged every step. The other is rewritten every step from a span and rebuilds
    // its layout each time. The buffers it grew on the first passes serve the rest.
    [Fact]
    public void ARichLabel_DrawsAndRebuildsItsLayoutAllocatingNothingOnceWarm()
    {
        Label steady = new(FontFixtures.Font(), "AB [color=#ff0000]A[color=#00ff00]B[/color][/color] [[A") { RichText = true };
        Label rewritten = new(FontFixtures.Font(), string.Empty) { RichText = true };

        Scene scene = new();
        scene.Add(new Rewriter(steady, rewritten));
        using SceneSimulation simulation = new(scene);

        StepSample[] samples = StepMeasurement.Measure(simulation, warmupSteps: 100, measuredSteps: 400);

        Assert.All(samples, sample => Assert.Equal(0, sample.ViewBytes));
        Assert.All(samples, sample => Assert.Equal(0, sample.StepBytes));
        Assert.True(samples[^1].Render.Visible > 0);
    }

    private sealed class Rewriter : Entity
    {
        private readonly Label _rewritten;
        private int _step;

        internal Rewriter(Label steady, Label rewritten)
        {
            _rewritten = rewritten;
            Add(steady);
            Add(rewritten);
        }

        protected internal override void OnStep(in StepContext context) =>
            _rewritten.SetText((_step++ & 1) == 0 ? "AB [color=#00ff00]AB[/color]" : "[color=#ff00ff]B[/color] A [color=#ff0000]BA");
    }
}
