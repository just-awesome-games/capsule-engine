using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Diagnostics;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Runtime;

public sealed class DebugSceneTests
{
    [Fact]
    public void DebugScene_WritesBackdropThenTheCachedReadoutGlyphsToTheScreenLayer()
    {
        DebugScene scene = new("Gameplay", 17, 3);
        using SimulationHost host = new(
            scene,
            new InputState(new ActionBindings()),
            run: new Run
            {
                Canvas = new Vector2(640f, 360f),
                Sampling = TextureSampling.Point,
            });

        host.Step(DeviceSnapshot.Empty);

        SpriteIntent[] sprites = host.Simulation.View.ScreenSprites.ToArray();
        SpriteIntent backdrop = sprites[0];

        Assert.Empty(host.Simulation.View.Sprites.ToArray());
        Assert.Equal(Sprite.White, backdrop.Sprite);
        Assert.Equal(new ColorRgba(0, 0, 0, 160), backdrop.Color);
        Assert.Equal(Vector2.Zero, backdrop.Position);
        Assert.Equal(
            new Vector2(BitmapFont.Default.Measure(scene.Readout).X + 8f, BitmapFont.Default.LineHeight + 8f),
            backdrop.Size);
        Assert.Equal(scene.Readout.Length + 1, sprites.Length);
        Assert.Equal(scene.Readout, scene.Label.Text);
        Assert.Equal(new Vector2(4f, 4f), sprites[1].Position);
        Assert.Equal(new Vector2(4f, 4f), sprites[1].PreviousPosition);
    }

    [Fact]
    public void DebugScene_ReadoutAllocatesOnlyWhenItsValuesChange()
    {
        DebugScene scene = new("Gameplay", 17, 3);

        string first = scene.Readout;
        scene.SetReadout("Gameplay", 17, 3);

        Assert.Same(first, scene.Readout);

        scene.SetReadout("Gameplay", 18, 3);

        Assert.NotSame(first, scene.Readout);
        Assert.Equal("Gameplay  tick 18  steps 3", scene.Readout);
    }

    [Fact]
    public void ResizingARunCanvas_ReanchorsAScreenEntityWithoutReplacingItsHost()
    {
        ScreenEntity entity = new(Anchor.Center, Vector2.Zero);
        ColorRect rect = new(new Vector2(2f, 2f));
        entity.Add(rect);

        Scene scene = new();
        scene.Add(entity);
        Run run = new() { Canvas = new Vector2(100f, 50f) };
        using SimulationHost host = new(scene, run: run);

        host.Step();

        Assert.Equal(new Vector2(50f, 25f), Assert.Single(host.Simulation.View.ScreenSprites.ToArray()).Position);

        run.Canvas = new Vector2(200f, 100f);
        host.Step();

        Assert.Same(scene, host.Scene);
        Assert.Equal(new Vector2(100f, 50f), Assert.Single(host.Simulation.View.ScreenSprites.ToArray()).Position);
    }
}
