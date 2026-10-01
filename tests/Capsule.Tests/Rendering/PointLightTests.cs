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

    // Bounds is the light where the next frame places it, not swept from where the last frame drew it,
    // and a light that draws nothing reports nothing.
    [Fact]
    public void Bounds_IsTheLightAtRest_AndEmptyAtZeroIntensity()
    {
        PointLight light = new() { Radius = 10f };
        SceneFixtures.Drifter lit = new(new Vector2(100, 50));
        lit.Add(light);
        SceneFixtures.HookScene scene = new(SceneFixtures.Opens(new Vector2(100, 50)));
        scene.Add(lit);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new Rect(91, 40, 111, 60), light.Bounds);

        light.Intensity = 0f;

        Assert.True(light.Bounds.IsEmpty);
    }

    [Fact]
    public void APointLightUnderAScreenEntity_ThrowsOnDraw()
    {
        ScreenEntity element = new(Anchor.TopLeft, Vector2.Zero);
        element.Add(new PointLight());

        SceneFixtures.HookScene scene = new();
        scene.Add(element);

        using SceneSimulation simulation = new(scene);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => simulation.View);
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
}
