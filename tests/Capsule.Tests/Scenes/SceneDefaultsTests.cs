using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class SceneDefaultsTests
{
    private static readonly SceneDefaults Defaults = new(TextureSampling.Point);

    [Fact]
    public void ASceneDeclaringNoSampling_OpensAtTheGamesDefault()
    {
        SceneSimulation simulation = new(new SceneFixtures.HookScene(), null, Defaults);

        Assert.Equal(TextureSampling.Point, simulation.View.Sampling);
    }

    [Fact]
    public void ASceneDeclaringItsOwnSampling_KeepsItOverTheGamesDefault()
    {
        SceneSimulation simulation = new(new ComposedScene(), null, Defaults);

        Assert.Equal(TextureSampling.Linear, simulation.View.Sampling);
    }

    private sealed class ComposedScene : Scene
    {
        internal ComposedScene() => Sampling = TextureSampling.Linear;
    }
}
