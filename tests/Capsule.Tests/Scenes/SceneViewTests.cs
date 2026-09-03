using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Tests.Scenes;

public sealed class SceneViewTests
{
    [Fact]
    public void RenderersDrawInSceneOrder_WhateverKindTheyAre()
    {
        SceneFixtures.Drifter first = new(new Vector2(7, 9));
        first.Add(new SpriteRenderer(SceneFixtures.Frame(4, 8)));
        SceneFixtures.Drifter second = new(new Vector2(2, 2));
        second.Add(new SceneFixtures.StripeRenderer(ColorRgba.Black));

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(second);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(2, simulation.View.Sprites.Length);

        SpriteIntent body = simulation.View.Sprites[0];
        Assert.Equal(new Vector2(7, 9), body.PreviousPosition);
        Assert.Equal(new Vector2(8, 9), body.Position);
        Assert.Equal(new Vector2(4, 8), body.Size);

        SpriteIntent stripe = simulation.View.Sprites[1];
        Assert.Equal(new Vector2(1f, 64f), stripe.Size);
        Assert.Equal(ColorRgba.Black, stripe.Color);
    }

    [Fact]
    public void ARendererAttachedToAnEarlyEntity_DrawsInThatEntitysPlace()
    {
        SceneFixtures.Drifter early = new(new Vector2(1, 1));
        SceneFixtures.Drifter late = new(new Vector2(2, 2));
        late.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)) { Color = ColorRgba.Black });

        SceneFixtures.HookScene scene = new();
        scene.Add(early);
        scene.Add(late);
        SceneSimulation simulation = new(scene);

        Assert.Equal(1, simulation.View.Sprites.Length);

        early.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(2, simulation.View.Sprites.Length);
        Assert.Equal(ColorRgba.White, simulation.View.Sprites[0].Color);
        Assert.Equal(ColorRgba.Black, simulation.View.Sprites[1].Color);
    }

    [Fact]
    public void AStepThatChangesNothingStructural_DrawsTheSameRenderersMoved()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(5, 5));
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        SceneFixtures.HookScene scene = new();
        scene.Add(drifter);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(1, simulation.View.Sprites.Length);
        Assert.Equal(new Vector2(6, 5), simulation.View.Sprites[0].PreviousPosition);
        Assert.Equal(new Vector2(7, 5), simulation.View.Sprites[0].Position);
    }

    [Fact]
    public void ARemovedEntity_LeavesTheFrameWithIt()
    {
        SceneFixtures.Drifter leaving = new(new Vector2(3, 3));
        leaving.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        SceneFixtures.HookScene scene = new();
        scene.Add(leaving);
        SceneSimulation simulation = new(scene);

        Assert.Equal(1, simulation.View.Sprites.Length);

        scene.Remove(leaving);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(0, simulation.View.Sprites.Length);
    }

    // Drawing runs past the end of the step, so a detach from inside Draw takes effect at once.
    // The detached renderer has left the scene and must not draw; the one behind it still must.
    [Fact]
    public void ARendererDetachingALaterRenderer_DrawsTheRestAndNotTheDetachedOne()
    {
        SceneFixtures.Drifter first = new(new Vector2(1, 1));
        SceneFixtures.Drifter second = new(new Vector2(2, 2));

        SpriteRenderer detached = new(SceneFixtures.Frame(1, 1)) { Color = ColorRgba.Black };
        second.Add(detached);
        second.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(second);
        using SceneSimulation simulation = new(scene);

        Assert.Equal(2, simulation.View.Sprites.Length);

        first.Add(new Detacher(detached));
        simulation.Step(SceneFixtures.Step());

        Assert.Null(detached.Entity);
        Assert.Equal(ColorRgba.White, Assert.Single(simulation.View.Sprites.ToArray()).Color);
    }

    [Fact]
    public void ARenderersOffset_MovesTheSpriteAndNotTheEntity()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(20, 20));
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)) { Offset = new Vector2(-4, -8) });

        SceneFixtures.HookScene scene = new();
        scene.Add(drifter);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        SpriteIntent body = simulation.View.Sprites[0];
        Assert.Equal(new Vector2(16, 12), body.PreviousPosition);
        Assert.Equal(new Vector2(17, 12), body.Position);
        Assert.Equal(new Vector2(21, 20), drifter.Position);
    }

    // The pivot is in region texels and the corner math scales it with the region, so a frame
    // anchored at its centre stays centred on the entity however large it is drawn.
    [Fact]
    public void AScaledSprite_KeepsItsPivotOnTheEntitysPosition()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(50, 50));
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8) with { Pivot = new Vector2(4, 4) })
        {
            Scale = new Vector2(2, 2),
        });

        SceneFixtures.HookScene scene = new(start: Open(new Vector2(50, 50)));
        scene.Add(drifter);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        SpriteIntent body = simulation.View.Sprites[0];

        Assert.Equal(new Vector2(16, 16), body.Size);
        Assert.Equal(new Vector2(51, 50), body.Position);

        // Twice the frame, still hung from its middle: the rect straddles the swept position by
        // eight world units on every side rather than hanging off one corner of it.
        Assert.True(body.TryGetSweptBounds(out ViewBounds swept));
        Assert.Equal(new ViewBounds(42f, 42f, 59f, 58f), swept);
    }

    // No validation on the setter: a scale that is not a size makes an extent the frame view
    // already refuses, so the sprite is culled rather than drawn inside out.
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-1f, 1f)]
    [InlineData(1f, float.NaN)]
    public void ASpriteScaledToNothing_DrawsNothing(float x, float y)
    {
        SceneFixtures.Drifter drifter = new(new Vector2(50, 50));
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8)) { Scale = new Vector2(x, y) });

        SceneFixtures.HookScene scene = new(start: Open(new Vector2(50, 50)));
        scene.Add(drifter);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Empty(simulation.View.Sprites.ToArray());
    }

    private static Action<Scene> Open(Vector2 center) => scene =>
    {
        scene.Camera.Center = center;
        scene.Camera.ViewportSize = new Vector2(320, 180);
    };

    // Draws nothing itself; takes the renderer it was given off its entity as it goes.
    private sealed class Detacher(Renderer doomed) : Renderer
    {
        public override void Draw(FrameView view) => doomed.Entity?.Remove(doomed);
    }
}
