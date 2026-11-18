using System.Numerics;
using Capsule.Animation;
using Capsule.Assets;
using Capsule.Particles;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Particles;

public sealed class ParticleLightTests
{
    // A light spans 2 * Radius at a particle scale of one, and the drawn scale, the fade and the mirror reach it as they
    // reach the sprite. A negative scale folds into the light's flip, where the sprite carries it in its size.
    [Fact]
    public void EachLiveParticle_AddsOneLightAtItsSpritesPositionScaleAndFade()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = ParticleEmitterTests.Build(capacity: 4);
        emitter.Lifetime = 1f;
        emitter.Speed = 30f;
        emitter.Spread = 360f;
        emitter.AngularVelocity = 90f;
        emitter.Scale = -2f;
        emitter.ScaleOverLifetime = Curve.Linear(1f, 0.5f);
        emitter.Color = Gradient.Linear(ColorRgba.White, ColorRgba.White with { A = 0 });
        emitter.Blend = BlendMode.Additive;
        emitter.Light = new ParticleLight(radius: 8f, intensity: 2f);
        emitter.Emit(3);

        host.Step(20);
        FrameView view = host.Simulation.View;

        Assert.Equal(3, view.Lights.Length);
        for (int index = 0; index < 3; index++)
        {
            SpriteIntent sprite = view.Sprites[index];
            LightIntent light = view.Lights[index];

            Assert.Equal(Sprite.Light, light.Sprite);
            Assert.Equal(sprite.PreviousPosition, light.PreviousPosition);
            Assert.Equal(sprite.Position, light.Position);
            Assert.Equal(sprite.PreviousRotation, light.PreviousRotation);
            Assert.Equal(sprite.Rotation, light.Rotation);
            Assert.Equal(Vector2.Abs(sprite.Size) * 16f / 4f, light.Size);
            Assert.Equal(sprite.FlipX != sprite.Size.X < 0f, light.FlipX);
            Assert.Equal(sprite.FlipY != sprite.Size.Y < 0f, light.FlipY);
            Assert.Equal(sprite.Color, light.Color);
            Assert.InRange(light.Color.A, 1, 254);
            Assert.Equal(2f, light.Intensity);
        }
    }

    // A clip-less emitter declares nothing to preload and draws no sprite, whichever frame mode it is given.
    [Theory]
    [InlineData(FrameMode.Clip)]
    [InlineData(FrameMode.InOrderAtSpawn)]
    public void ALightOnlyEmitter_CollectsNoSpriteAndDrawsOnlyLights(FrameMode mode)
    {
        ParticleEmitter emitter = new(capacity: 4) { FrameMode = mode, Light = new ParticleLight(radius: 8f) };
        AssetCollection assets = new();
        emitter.CollectAssets(assets);
        (_, _, SimulationHost host) = ParticleEmitterTests.Build(emitter);
        emitter.Emit(2);

        host.Step();
        FrameView view = host.Simulation.View;

        Assert.Empty(assets.Textures);
        Assert.Equal(0, view.Sprites.Length);
        Assert.Equal(2, view.Lights.Length);
    }

    [Fact]
    public void ALitEmitterUnderAScreenEntity_ThrowsOnDrawWithNothingAlive()
    {
        ScreenEntity element = new(Anchor.TopLeft, Vector2.Zero);
        element.Add(new ParticleEmitter(SceneFixtures.Frame(4, 4), capacity: 4) { Light = new ParticleLight(radius: 8f) });
        SceneFixtures.HookScene scene = new();
        scene.Add(element);

        using SceneSimulation simulation = new(scene);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => simulation.View);
        Assert.Contains("screen layer is never lit", exception.Message);
    }

    // The particle's sprite ends 16 units short of the view, and its light reaches 20 units into it. The emitter's
    // cull once grew only by the sprite's reach and dropped the light with it.
    [Fact]
    public void ALightReachingTheView_DrawsWhenItsParticlesSpriteMissesIt()
    {
        (_, ParticleEmitter emitter, SimulationHost host) = ParticleEmitterTests.Build(capacity: 1);
        emitter.Lifetime = 5f;
        emitter.Light = new ParticleLight(radius: 30f);
        emitter.Emit(1, new Vector2(-40f, 0f));

        host.Step();
        FrameView view = new() { Camera = new CameraView(Vector2.Zero, new Vector2(40f, 40f)) };
        emitter.Draw(view);

        Assert.Equal(0, view.Sprites.Length);
        Assert.Equal(1, view.Lights.Length);
    }
}
