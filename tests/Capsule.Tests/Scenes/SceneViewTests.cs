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
        first.Add(new QuadRenderer(new Vector2(4, 8), ColorRgba.White));
        SceneFixtures.Drifter second = new(new Vector2(2, 2));
        second.Add(new SceneFixtures.StripeRenderer(ColorRgba.Black));

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(second);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(2, simulation.View.Quads.Length);

        QuadIntent body = simulation.View.Quads[0];
        Assert.Equal(new Vector2(7, 9), body.PreviousPosition);
        Assert.Equal(new Vector2(8, 9), body.Position);
        Assert.Equal(new Vector2(4, 8), body.Size);

        QuadIntent stripe = simulation.View.Quads[1];
        Assert.Equal(new Vector2(1f, 64f), stripe.Size);
        Assert.Equal(ColorRgba.Black, stripe.Color);
    }

    [Fact]
    public void ARendererAttachedToAnEarlyEntity_DrawsInThatEntitysPlace()
    {
        SceneFixtures.Drifter early = new(new Vector2(1, 1));
        SceneFixtures.Drifter late = new(new Vector2(2, 2));
        late.Add(new QuadRenderer(Vector2.One, ColorRgba.Black));

        SceneFixtures.HookScene scene = new();
        scene.Add(early);
        scene.Add(late);
        SceneSimulation simulation = new(scene);

        Assert.Equal(1, simulation.View.Quads.Length);

        early.Add(new QuadRenderer(Vector2.One, ColorRgba.White));
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(2, simulation.View.Quads.Length);
        Assert.Equal(ColorRgba.White, simulation.View.Quads[0].Color);
        Assert.Equal(ColorRgba.Black, simulation.View.Quads[1].Color);
    }

    [Fact]
    public void AStepThatChangesNothingStructural_DrawsTheSameRenderersMoved()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(5, 5));
        drifter.Add(new QuadRenderer(Vector2.One, ColorRgba.White));

        SceneFixtures.HookScene scene = new();
        scene.Add(drifter);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal(1, simulation.View.Quads.Length);
        Assert.Equal(new Vector2(6, 5), simulation.View.Quads[0].PreviousPosition);
        Assert.Equal(new Vector2(7, 5), simulation.View.Quads[0].Position);
    }

    [Fact]
    public void ARemovedEntity_LeavesTheFrameWithIt()
    {
        SceneFixtures.Drifter leaving = new(new Vector2(3, 3));
        leaving.Add(new QuadRenderer(Vector2.One, ColorRgba.White));

        SceneFixtures.HookScene scene = new();
        scene.Add(leaving);
        SceneSimulation simulation = new(scene);

        Assert.Equal(1, simulation.View.Quads.Length);

        scene.Remove(leaving);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(0, simulation.View.Quads.Length);
    }

    [Fact]
    public void ARenderersOffset_MovesTheQuadAndNotTheEntity()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(20, 20));
        drifter.Add(new QuadRenderer(Vector2.One, ColorRgba.White) { Offset = new Vector2(-4, -8) });

        SceneFixtures.HookScene scene = new();
        scene.Add(drifter);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        QuadIntent body = simulation.View.Quads[0];
        Assert.Equal(new Vector2(16, 12), body.PreviousPosition);
        Assert.Equal(new Vector2(17, 12), body.Position);
        Assert.Equal(new Vector2(21, 20), drifter.Position);
    }
}
