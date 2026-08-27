using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The game-level defaults a scene opens at, and the precedence between them and a scene that
/// declares its own. What a game declares none of is covered by the scenes elsewhere in this
/// suite, every one of which runs on the default value.
/// </summary>
public sealed class SceneDefaultsTests
{
    private static readonly SceneDefaults Defaults = new(new Vector2(320, 180), TextureSampling.Point);

    [Fact]
    public void ASceneDeclaringNeither_OpensAtTheGamesDefaults()
    {
        SceneSimulation simulation = new(new SceneFixtures.HookScene(), null, Defaults);

        Assert.Equal(new CameraView(Vector2.Zero, new Vector2(320, 180)), simulation.View.Camera);
        Assert.Equal(TextureSampling.Point, simulation.View.Sampling);
    }

    // A scene composes itself in its constructor, which runs before the defaults are handed to it,
    // so "the scene said nothing" has to survive that gap rather than being read off the value.
    [Fact]
    public void ASceneDeclaringItsOwn_KeepsThemOverTheGamesDefaults()
    {
        SceneSimulation simulation = new(new ComposedScene(), null, Defaults);

        Assert.Equal(new Vector2(64, 36), simulation.View.Camera.Size);
        Assert.Equal(TextureSampling.Linear, simulation.View.Sampling);
    }

    // Spanning nothing is a scene's own answer, not the absence of one: a cutscene that draws
    // through no camera must not have the game default filled in behind its back.
    [Fact]
    public void ASceneSpanningNothingDeliberately_IsNotTakenForUnset()
    {
        SceneSimulation simulation = new(new BlindScene(), null, Defaults);

        Assert.Equal(Vector2.Zero, simulation.View.Camera.Size);
    }

    private sealed class ComposedScene : Scene
    {
        internal ComposedScene()
        {
            Camera.ViewportSize = new Vector2(64, 36);
            Sampling = TextureSampling.Linear;
        }
    }

    private sealed class BlindScene : Scene
    {
        internal BlindScene() => Camera.ViewportSize = Vector2.Zero;
    }
}
