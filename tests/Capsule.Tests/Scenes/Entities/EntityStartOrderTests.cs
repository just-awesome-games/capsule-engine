using System.Numerics;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;
using Recorder = Capsule.Tests.Scenes.SceneFixtures.Recorder;
using Watcher = Capsule.Tests.Scenes.SceneFixtures.Watcher;

namespace Capsule.Tests.Scenes;

public sealed class EntityStartOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(true, true)]
    public void AParentWriteBeforeTheStart_StartsTheParentFirst_WhateverTheAddOrder(bool inJoinHook, bool nestedInRunningScene = false)
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        if (nestedInRunningScene)
        {
            using SceneSimulation running = new(scene);
            running.Step(SceneFixtures.Step());
            Adopter outer = new(null, log) { Spawns = new Joiner(log) };
            scene.Add(outer);
            Assert.Equal(["parent!", "child!"], log);
            return;
        }

        Recorder child = new("child", log, logsStart: true);
        Adopter parent = new(inJoinHook ? child : null, log);
        scene.Add(child);
        scene.Add(parent);
        if (!inJoinHook)
        {
            child.Parent = parent;
        }

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["parent!", "child!"], log.Where(static entry => entry.EndsWith('!')));
    }

    [Fact]
    public void AReparentOfQueuedEntities_StartsThemInTreeOrder_NotQueueOrder()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        Node first = new(Vector2.Zero);
        Node second = new(Vector2.Zero);
        Recorder x = new("x", log, logsStart: true);
        Recorder y = new("y", log, logsStart: true);
        int steps = 0;
        scene.Add(first);
        scene.Add(second);
        scene.Add(new Watcher(_ =>
        {
            if (++steps == 2)
            {
                scene.Add(x);
                scene.Add(y);
                x.Parent = second;
                y.Parent = first;
            }
        }));

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        log.Clear();
        simulation.Step(SceneFixtures.Step(tick: 1));

        Assert.Equal(["y!", "x!"], log.Where(static entry => entry.EndsWith('!')));
    }

    [Fact]
    public void AChildBuiltInItsParentsJoinHook_StartsAfterTheParent()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        scene.Add(new Builder(log));

        Assert.Equal(["parent!", "child!"], log.Where(static entry => entry.EndsWith('!')));
    }

    private sealed class Joiner(List<string> log) : Entity(Vector2.Zero)
    {
        internal Entity? Adopter { get; set; }

        protected internal override void OnAddedToScene() => Parent = Adopter;

        protected internal override void OnStart() => log.Add("child!");
    }

    private sealed class Builder(List<string> log) : Entity(Vector2.Zero)
    {
        protected internal override void OnAddedToScene() => _ = new Recorder("child", log, logsStart: true) { Parent = this };

        protected internal override void OnStart() => log.Add("parent!");
    }

    private sealed class Adopter(Entity? child, List<string> log) : Entity(Vector2.Zero)
    {
        internal Joiner? Spawns { get; init; }

        protected internal override void OnAddedToScene()
        {
            if (Spawns is not null)
            {
                Spawns.Adopter = this;
                Scene!.Add(Spawns);
            }

            if (child is not null)
            {
                child.Parent = this;
            }
        }

        protected internal override void OnStart() => log.Add("parent!");
    }
}
