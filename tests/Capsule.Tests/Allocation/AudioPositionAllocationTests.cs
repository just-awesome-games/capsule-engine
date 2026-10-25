using System.Numerics;
using Capsule.Animation;
using Capsule.Audio;
using Capsule.Scenes;
using Capsule.Tests.Audio;
using Capsule.Tests.Scenes;
using Xunit.Abstractions;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class AudioPositionAllocationTests(ITestOutputHelper output)
{
    // Every source moves every step. Each step then settles a new gain and pan for each voice.
    [Fact]
    public void AStepMovingPositionalSources_AllocatesNothing()
    {
        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(Vector2.Zero));
        for (int i = 0; i < 8; i++)
        {
            SceneFixtures.Drifter drifter = new(new Vector2(-400f + (i * 10f), i));
            drifter.Add(new AudioSource(AudioMixerFixtures.Theme)
            {
                Loop = true,
                AutoPlay = true,
                Range = new AudioRange(10000f) { Falloff = Ease.InQuad },
            });
            scene.Add(drifter);
        }

        using SimulationHost run = new(scene);

        long bytes = StepMeasurement.Measure(run.Simulation).Sum(static sample => sample.StepBytes);

        StepMeasurement.Report(output, "a step moving positional sources", bytes);
        Assert.NotEmpty(AudioMixerFixtures.Raised(run.Run.Audio));
    }
}
