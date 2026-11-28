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
            // A child the parent's join hook adds and one it builds by a parent write both start after it.
            using SceneSimulation running = new(scene);
            running.Step(SceneFixtures.Step());
            Adopter outer = new(new Recorder("built", log, logsStart: true), log) { Spawns = new Joiner(log) };
            scene.Add(outer);
            Assert.Equal(["parent!", "child!", "built!"], log.Where(static entry => entry.EndsWith('!')));
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

    // Each rejoin gives the entity a new place in the tree, and its place in the wait follows it: one
    // sent to the end, one brought before the rest under the entity starting, and two sent to the end.
    [Theory]
    [InlineData(0, "c!", "d!", "b!")]
    [InlineData(1, "d!", "b!", "c!")]
    [InlineData(2, "d!", "b!", "c!")]
    public void EntitiesRemovedAndAddedBackByAnEarlierStart_StartWhereTheyThenStand(int rejoins, params string[] expected)
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Node root = new(Vector2.Zero);
        Recorder b = new("b", log, logsStart: true);
        Recorder c = new("c", log, logsStart: true);
        Recorder d = new("d", log, logsStart: true);
        SceneFixtures.Starter? first = null;
        first = new SceneFixtures.Starter(_ =>
        {
            switch (rejoins)
            {
                case 0:
                    scene.Remove(b);
                    scene.Add(b);
                    break;
                case 1:
                    scene.Remove(d);
                    d.Parent = first;
                    break;
                default:
                    scene.Remove(b);
                    scene.Add(b);
                    scene.Remove(c);
                    scene.Add(c);
                    break;
            }
        })
        {
            Parent = root,
        };
        b.Parent = root;
        c.Parent = root;
        d.Parent = root;
        scene.Add(root);

        Assert.Equal(expected, log.Where(static entry => entry.EndsWith('!')));
    }

    private sealed class Joiner(List<string> log) : Entity(Vector2.Zero)
    {
        internal Entity? Adopter { get; set; }

        protected internal override void OnAddedToScene() => Parent = Adopter;

        protected internal override void OnStart() => log.Add("child!");
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
