using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Rendering;

namespace Capsule.Tests.Scenes;

public sealed class SceneWorldTests
{
    [Fact]
    public void TypedQueries_UseInsertionOrderAndRequireUniquenessWhenAsked()
    {
        Scene scene = new();
        TestEntity first = new();
        DerivedEntity second = new();
        scene.Add(first);
        scene.Add(second);

        Assert.Same(first, scene.FindFirst<TestEntity>());
        Assert.Same(second, scene.FindSingle<DerivedEntity>());
        Assert.Throws<InvalidOperationException>(() => scene.FindSingle<TestEntity>());
        Assert.Throws<InvalidOperationException>(() => scene.FindSingle<MissingEntity>());
    }

    [Fact]
    public void DisposingASimulation_StopsOnceThenReleasesEveryEntity()
    {
        List<string> log = [];
        LifecycleScene scene = new(log);
        SceneFixtures.Recorder first = new("first", log);
        SceneFixtures.Recorder second = new("second", log);
        scene.Add(first);
        scene.Add(second);
        SceneSimulation simulation = new(scene);
        log.Clear();

        simulation.Dispose();
        simulation.Dispose();

        Assert.Equal(3, log.Count);
        Assert.Equal("scene-", log[0]);
        Assert.Contains("first-", log);
        Assert.Contains("second-", log);
        Assert.Empty(scene.Entities.ToArray());
        Assert.Null(first.Scene);
        Assert.Null(second.Scene);
        Assert.Throws<ObjectDisposedException>(() => simulation.Step(SceneFixtures.Step()));
    }

    [Fact]
    public void CleanupFailures_DoNotLeaveLaterEntitiesAttached()
    {
        ThrowingLifecycleScene scene = new();
        ThrowingRemovalEntity throwing = new();
        TestEntity ordinary = new();
        scene.Add(ordinary);
        scene.Add(throwing);
        SceneSimulation simulation = new(scene);

        AggregateException failure = Assert.Throws<AggregateException>(() => simulation.Dispose());

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Empty(scene.Entities.ToArray());
        Assert.Null(ordinary.Scene);
        Assert.Null(throwing.Scene);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void RemovalFailures_StillReleaseComponentsAndAllowReattachment(bool stop, bool entityFails, bool componentFails)
    {
        Scene scene = new();
        SceneFixtures.Open(scene, Vector2.Zero, new Vector2(100f));
        RemovalFailureEntity entity = new() { Fail = entityFails };
        RemovalFailureComponent component = new() { Fail = componentFails };
        BoxCollider2D collider = new(new Vector2(4f));
        VisibleOnScreenNotifier2D notifier = new(new Vector2(4f));
        entity.Add(component);
        entity.Add(collider);
        entity.Add(notifier);
        int entered = 0;
        int exited = 0;
        notifier.ScreenEntered += () => entered++;
        notifier.ScreenExited += () => exited++;
        scene.Add(entity);
        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        Assert.True(notifier.IsOnScreen);

        Exception? failure = Record.Exception(() =>
        {
            if (stop)
            {
                simulation.Dispose();
            }
            else
            {
                scene.Remove(entity);
            }
        });

        if (entityFails && componentFails)
        {
            AggregateException aggregate = Assert.IsType<AggregateException>(failure);
            Assert.Equal(2, aggregate.Flatten().InnerExceptions.Count);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(failure);
        }

        Assert.Equal(1, component.Removals);
        Assert.Null(entity.Scene);
        Assert.Null(collider.World);
        Assert.False(notifier.IsOnScreen);
        Assert.Equal(1, exited);

        entity.Fail = false;
        component.Fail = false;
        Scene next = new();
        SceneFixtures.Open(next, Vector2.Zero, new Vector2(100f));
        next.Add(entity);
        using SceneSimulation nextSimulation = new(next);
        nextSimulation.Step(SceneFixtures.Step());

        Assert.Same(next.Collision, collider.World);
        Assert.True(notifier.IsOnScreen);
        Assert.Equal(2, entered);
        simulation.Dispose();
    }

    [Fact]
    public void SceneRenderIntent_IsCopiedIntoEveryRewrittenView()
    {
        RenderIntentScene scene = new();
        SceneSimulation simulation = new(scene);

        Assert.Equal(new ColorRgba(12, 24, 36), simulation.View.ClearColor);
        Assert.Equal(TextureSampling.Point, simulation.View.Sampling);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new ColorRgba(12, 24, 36), simulation.View.ClearColor);
        Assert.Equal(TextureSampling.Point, simulation.View.Sampling);
    }

    private class TestEntity() : Entity(Vector2.Zero);

    private sealed class DerivedEntity : TestEntity;

    private sealed class MissingEntity() : Entity(Vector2.Zero);

    private sealed class LifecycleScene(List<string> log) : Scene
    {
        protected override void OnStop() => log.Add("scene-");
    }

    private sealed class RenderIntentScene : Scene
    {
        internal RenderIntentScene()
        {
            ClearColor = new ColorRgba(12, 24, 36);
            Sampling = TextureSampling.Point;
        }
    }

    private sealed class ThrowingLifecycleScene : Scene
    {
        protected override void OnStop() => throw new InvalidOperationException("scene cleanup failed");
    }

    private sealed class RemovalFailureEntity() : Entity(Vector2.Zero)
    {
        internal bool Fail { get; set; }

        protected internal override void OnRemovedFromScene()
        {
            if (Fail)
            {
                throw new InvalidOperationException("entity cleanup failed");
            }
        }
    }

    private sealed class RemovalFailureComponent : Component
    {
        internal bool Fail { get; set; }

        internal int Removals { get; private set; }

        protected internal override void OnRemovedFromScene()
        {
            Removals++;
            if (Fail)
            {
                throw new InvalidOperationException("component cleanup failed");
            }
        }
    }

    private sealed class ThrowingRemovalEntity() : Entity(Vector2.Zero)
    {
        protected internal override void OnRemovedFromScene() => throw new InvalidOperationException("entity cleanup failed");
    }
}
