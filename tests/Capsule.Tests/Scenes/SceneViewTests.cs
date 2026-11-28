using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

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

        SpriteIntent[] drawn = Drawn(null, first, second);

        Assert.Equal(2, drawn.Length);

        SpriteIntent body = drawn[0];
        Assert.Equal(new Vector2(7, 9), body.PreviousPosition);
        Assert.Equal(new Vector2(8, 9), body.Position);
        Assert.Equal(new Vector2(4, 8), body.Size);

        SpriteIntent stripe = drawn[1];
        Assert.Equal(new Vector2(1f, 64f), stripe.Size);
        Assert.Equal(ColorRgba.Black, stripe.Color);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARemovedEntityOrRenderer_LeavesTheFrameWithIt(bool rendererAlone)
    {
        SceneFixtures.Drifter leaving = new(new Vector2(3, 3));
        SpriteRenderer renderer = new(SceneFixtures.Frame(1, 1));
        leaving.Add(renderer);

        SceneFixtures.HookScene scene = new();
        scene.Add(leaving);
        SceneSimulation simulation = new(scene);

        Assert.Equal(1, simulation.View.Sprites.Length);

        if (rendererAlone)
        {
            leaving.Remove(renderer);
        }
        else
        {
            scene.Remove(leaving);
        }

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(0, simulation.View.Sprites.Length);
    }

    [Fact]
    public void ARenderersOffset_MovesTheSpriteAndNotTheEntity()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(20, 20));
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)) { Offset = new Vector2(-4, -8) });

        SpriteIntent body = Drawn(null, drifter)[0];
        Assert.Equal(new Vector2(16, 12), body.PreviousPosition);
        Assert.Equal(new Vector2(17, 12), body.Position);
        Assert.Equal(new Vector2(21, 20), drifter.Position);
    }

    // The pivot is in region texels and the corner math scales it with the region. A frame
    // anchored at its centre stays centred on the entity however large it is drawn.
    [Fact]
    public void AScaledSprite_KeepsItsPivotOnTheEntitysPosition()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(50, 50)) { Scale = new Vector2(2, 2) };
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8) with { Pivot = new Vector2(4, 4) }));

        SpriteIntent body = Drawn(new Vector2(50, 50), drifter)[0];

        Assert.Equal(new Vector2(16, 16), body.Size);
        Assert.Equal(new Vector2(51, 50), body.Position);

        // Twice the frame, still hung from its middle: the rect straddles the swept position by
        // eight world units on every side rather than hanging off one corner of it.
        Assert.True(body.TryGetSweptBounds(out Rect swept));
        Assert.Equal(Rect.FromEdges(42f, 42f, 59f, 58f), swept);
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

        // Pivot (6, 4) once mirrored. Two region columns and four rows of the frame hang past the
        // position it is drawn at, each at its own axis's scale. The offset is scaled with them.
        new SceneFixtures.Drifter(new Vector2(50, 50)) { Scale = new Vector2(2, 3) }.Add(sprite);

        Assert.Equal(Rect.FromEdges(40f, 38f, 56f, 50f), sprite.Bounds);
    }

    // A zero axis of the entity's scale makes an extent the frame view already refuses. The
    // sprite is culled rather than drawn inside out.
    [Fact]
    public void ASpriteScaledToNothing_DrawsNothing()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(50, 50)) { Scale = new Vector2(0f, 1f) };
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8)));

        Assert.Empty(Drawn(new Vector2(50, 50), drifter));
    }

    // A negative axis is a mirror about the pivot, exactly what a flip is. It folds into the
    // flip and the frame keeps its extent.
    [Fact]
    public void ANegativeEntityScale_MirrorsTheFrameAboutItsPivot()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(50, 50)) { Scale = new Vector2(-1f, 1f) };
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(8, 8)) { FlipX = true });

        SpriteIntent sprite = Assert.Single(Drawn(new Vector2(50, 50), drifter));
        Assert.Equal(new Vector2(8f, 8f), sprite.Size);
        Assert.False(sprite.FlipX);
    }

    // The frame a scene of entities draws after one step, its camera opened on a centre when given one.
    private static SpriteIntent[] Drawn(Vector2? center, params Entity[] entities)
    {
        SceneFixtures.HookScene scene = new(start: center is { } at ? SceneFixtures.Opens(at) : null);
        using SceneSimulation simulation = SceneFixtures.Simulation(scene, entities);
        simulation.Step(SceneFixtures.Step());

        return simulation.View.Sprites.ToArray();
    }
}
