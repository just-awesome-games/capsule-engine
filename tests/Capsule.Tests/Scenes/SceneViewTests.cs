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
        SceneFixtures.Drifter drifter = new(new Vector2(50, 50)) { Scale = new Vector2(2, 2) };
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8) with { Pivot = new Vector2(4, 4) }));

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(new Vector2(50, 50)));
        scene.Add(drifter);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        SpriteIntent body = simulation.View.Sprites[0];

        Assert.Equal(new Vector2(16, 16), body.Size);
        Assert.Equal(new Vector2(51, 50), body.Position);

        // Twice the frame, still hung from its middle: the rect straddles the swept position by
        // eight world units on every side rather than hanging off one corner of it.
        Assert.True(body.TryGetSweptBounds(out Rect swept));
        Assert.Equal(new Rect(42f, 42f, 59f, 58f), swept);
    }

    // The rect a pointer hit-tests a sprite against: at rest on the current position, the pivot
    // mirrored by the flip, and the region at its drawn extent.
    [Fact]
    public void AFlippedScaledSprite_ReportsTheRectItsFrameCovers()
    {
        SpriteRenderer sprite = new(SceneFixtures.Frame(8, 4) with { Pivot = new Vector2(2, 4) })
        {
            Offset = new Vector2(1, 0),
            FlipX = true,
        };

        Assert.True(sprite.Bounds.IsEmpty);

        // Pivot (6, 4) once mirrored, so two region columns and four rows of the frame hang past the
        // position it is drawn at, each at its own axis's scale; the offset is scaled with them.
        new SceneFixtures.Drifter(new Vector2(50, 50)) { Scale = new Vector2(2, 3) }.Add(sprite);

        Assert.Equal(new Rect(40f, 38f, 56f, 50f), sprite.Bounds);
    }

    // A zero axis of the entity's scale makes an extent the frame view already refuses, so the
    // sprite is culled rather than drawn inside out.
    [Fact]
    public void ASpriteScaledToNothing_DrawsNothing()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(50, 50)) { Scale = new Vector2(0f, 1f) };
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8)));

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(new Vector2(50, 50)));
        scene.Add(drifter);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Empty(simulation.View.Sprites.ToArray());
    }

    // A negative axis is a mirror about the pivot, exactly what a flip is, so it folds into the
    // flip and the frame keeps its extent.
    [Fact]
    public void ANegativeEntityScale_MirrorsTheFrameAboutItsPivot()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(50, 50)) { Scale = new Vector2(-1f, 1f) };
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8)) { FlipX = true });

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(new Vector2(50, 50)));
        scene.Add(drifter);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        SpriteIntent sprite = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(new Vector2(8f, 8f), sprite.Size);
        Assert.False(sprite.FlipX);
    }

    // Draws nothing itself; takes the renderer it was given off its entity as it goes.
    private sealed class Detacher(Renderer doomed) : Renderer
    {
        protected internal override void Draw(FrameView view) => doomed.Entity?.Remove(doomed);
    }
}
