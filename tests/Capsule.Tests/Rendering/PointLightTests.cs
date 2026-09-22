using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Rendering;

public sealed class PointLightTests
{
    [Fact]
    public void APointLightInsideTheCamera_LandsWithInterpolatedPositionAndRadius_OneOutsideIsCulled()
    {
        SceneFixtures.Drifter lit = new(new Vector2(100, 50));
        lit.Add(new PointLight { Radius = 10f, Color = ColorRgba.Orange });

        SceneFixtures.Drifter outside = new(new Vector2(100_000, 100_000));
        outside.Add(new PointLight { Radius = 10f });

        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(100, 50)));
        scene.Add(lit);
        scene.Add(outside);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        FrameView view = simulation.View;
        Assert.Equal(1, view.Lights.Length);
        LightIntent light = view.Lights[0];
        Assert.Equal(new Vector2(100, 50), light.PreviousPosition);
        Assert.Equal(new Vector2(101, 50), light.Position);
        Assert.Equal(10f, light.Radius);
    }

    // The headless/windowed identity: the host never touches the light list, so two identically
    // stepped simulations of the same scene hold equal lights and ambient.
    [Fact]
    public void TwoSimulationsSteppedTheSame_HoldEqualLightsAndAmbient()
    {
        SceneSimulation first = new(new LitScene());
        SceneSimulation second = new(new LitScene());

        first.Step(SceneFixtures.Step());
        second.Step(SceneFixtures.Step());

        Assert.Equal(second.View.Lights.ToArray(), first.View.Lights.ToArray());
        Assert.Equal(second.View.Ambient, first.View.Ambient);
    }

    [Fact]
    public void APointLightUnderAScreenEntity_ThrowsOnDraw()
    {
        ScreenEntity element = new(Anchor.TopLeft, Vector2.Zero);
        element.Add(new PointLight());

        SceneFixtures.HookScene scene = new();
        scene.Add(element);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new SceneSimulation(scene));
        Assert.Contains("screen layer is never lit", exception.Message);
    }

    [Fact]
    public void ASceneThatSetsNothing_ReportsWhiteAmbientNoLightsAndUnlit()
    {
        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(50, 50)));
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(ColorRgba.White, simulation.View.Ambient);
        Assert.Equal(0, simulation.View.Lights.Length);
        Assert.False(simulation.View.LitWorld);
    }

    [Fact]
    public void OneAdditiveWorldColorRect_DoesNotFlipLitWorldAlone()
    {
        SceneFixtures.Drifter glow = new(new Vector2(50, 50));
        glow.Add(new ColorRect(new Vector2(4, 4)) { Blend = BlendMode.Additive });

        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(50, 50)));
        scene.Add(glow);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.False(simulation.View.LitWorld);
    }

    [Fact]
    public void ATiledAdditiveSprite_KeepsItsBlendOnEveryCopy()
    {
        SceneFixtures.Drifter glow = new(new Vector2(50, 50));
        glow.Add(new SpriteRenderer(Sprite.White) { Blend = BlendMode.Additive, Tiling = new Vector2(3f, 0f) });

        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(50, 50)));
        scene.Add(glow);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.False(simulation.View.LitWorld);
        foreach (SpriteIntent copy in simulation.View.Sprites)
        {
            Assert.Equal(BlendMode.Additive, copy.Blend);
        }
    }

    [Fact]
    public void ALightOnAScrolledEntity_StartsAParallaxLayerIndexingIt()
    {
        SceneFixtures.Drifter scrolled = new(new Vector2(100, 50)) { ScrollFactor = new Vector2(0.5f, 1f) };
        scrolled.Add(new PointLight { Radius = 20f });

        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(100, 50)));
        scene.Add(scrolled);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        FrameView view = simulation.View;
        Assert.Equal(1, view.Lights.Length);
        Assert.Contains(view.ParallaxLayers.ToArray(), layer => layer.FirstLight == 0);
    }

    private sealed class LitScene : Scene
    {
        internal LitScene()
        {
            Ambient = new ColorRgba(64, 68, 96);
            Camera.Center = new Vector2(50, 50);
            Camera.ViewportSize = SceneFixtures.Viewport;
            Add(new LightEntity(new Vector2(50, 50), 12f));
        }
    }

    private sealed class LightEntity : Entity
    {
        internal LightEntity(Vector2 position, float radius)
            : base(position) =>
            Add(new PointLight { Radius = radius, Color = ColorRgba.Cyan });
    }
}
