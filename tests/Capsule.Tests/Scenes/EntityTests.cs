using System.Numerics;
using Capsule.Collision;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;

namespace Capsule.Tests.Scenes;

public sealed class EntityTests
{
    [Fact]
    public void Teleport_ResetsBothEndsOfInterpolation()
    {
        TestEntity entity = new(new Vector2(4, 6));
        entity.Position = new Vector2(8, 10);

        entity.Teleport(new Vector2(40, 50));

        Assert.Equal(new Vector2(40, 50), entity.Position);
        Assert.Equal(entity.Position, entity.PreviousPosition);
    }

    // Everything downstream reads this position — render interpolation and, through a collider,
    // the collision broadphase — so it is refused rather than stored and spread.
    [Fact]
    public void APositionThatIsNotFinite_IsRefusedAndLeavesTheEntityWhereItWas()
    {
        TestEntity entity = new(new Vector2(4, 6));

        Assert.Throws<ArgumentOutOfRangeException>(() => entity.Position = new Vector2(float.NaN, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => entity.Position = new Vector2(0f, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => entity.Teleport(new Vector2(float.NaN, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestEntity(new Vector2(float.NaN, 0f)));

        Assert.Equal(new Vector2(4, 6), entity.Position);
        Assert.Equal(new Vector2(4, 6), entity.PreviousPosition);
    }

    [Fact]
    public void APositionThatIsNotFinite_IsRefusedBeforeACollidingEntitysWorldHearsOfIt()
    {
        Scene scene = new();
        TestEntity entity = new(new Vector2(4, 6));
        Collider collider = new(new Vector2(8f, 8f));
        entity.Add(collider);
        scene.Add(entity);

        Assert.Throws<ArgumentOutOfRangeException>(() => entity.Position = new Vector2(float.NaN, 0f));

        Assert.Equal(new Vector2(4, 6), entity.Position);
        Assert.Equal(new Vector2(4, 6), scene.Collision.PositionOf(collider.Handle));
    }

    // Finiteness is not the whole of it: a position every float accepts can still be one an
    // attached collider cannot place its shape at, and the entity must not have moved by the time
    // anyone finds that out.
    [Fact]
    public void APositionAnAttachedColliderCannotBePlacedAt_LeavesTheEntityAndItsProxyWhereTheyWere()
    {
        Scene scene = new();
        TestEntity entity = new(new Vector2(4, 6));
        Collider collider = new(Shape.Circle(Vector2.Zero, 8e37f));
        entity.Add(collider);
        scene.Add(entity);

        Assert.Throws<ArgumentException>(() => entity.Position = new Vector2(3e38f, 0f));

        Assert.Equal(new Vector2(4, 6), entity.Position);
        Assert.Equal(new Vector2(4, 6), scene.Collision.PositionOf(collider.Handle));
    }

    // The preflight asks every collider before any of them moves, so one that refuses cannot leave
    // an earlier one already somewhere else.
    [Fact]
    public void ASecondColliderRefusingThePosition_LeavesTheFirstOneWhereItWas()
    {
        Scene scene = new();
        TestEntity entity = new(new Vector2(4, 6));
        Collider ordinary = new(new Vector2(8f, 8f));
        Collider enormous = new(Shape.Circle(Vector2.Zero, 8e37f));
        entity.Add(ordinary);
        entity.Add(enormous);
        scene.Add(entity);

        Assert.Throws<ArgumentException>(() => entity.Position = new Vector2(3e38f, 0f));

        Assert.Equal(new Vector2(4, 6), entity.Position);
        Assert.Equal(new Vector2(4, 6), scene.Collision.PositionOf(ordinary.Handle));
        Assert.Equal(new Vector2(4, 6), scene.Collision.PositionOf(enormous.Handle));
    }

    // Whether a shape has a place at a position is a fact about the shape and the position, not
    // about scene membership — so it is answered wherever the position is written, and a collider
    // that accepted one out of a scene can never turn round and refuse it on joining.
    [Fact]
    public void APositionAnAttachedColliderCannotBePlacedAt_IsRefusedOutOfASceneToo()
    {
        TestEntity entity = new(new Vector2(4, 6));
        entity.Add(new Collider(Shape.Circle(Vector2.Zero, 8e37f)));

        Assert.Throws<ArgumentException>(() => entity.Position = new Vector2(3e38f, 0f));
        Assert.Equal(new Vector2(4, 6), entity.Position);

        // A position it does accept while detached is one joining a scene cannot fail on.
        entity.Position = new Vector2(40, 50);

        Scene scene = new();
        scene.Add(entity);

        Assert.Same(scene, entity.Scene);
        Assert.Equal(new Vector2(40, 50), entity.Position);
    }

    [Fact]
    public void AColliderThatCannotStandWhereItsEntityIs_IsRefusedAtAttachRatherThanLater()
    {
        TestEntity entity = new(new Vector2(3e38f, 0f));

        Assert.Throws<ArgumentException>(() => entity.Add(new Collider(Shape.Circle(Vector2.Zero, 8e37f))));

        // Nothing was taken hold of, so no collider tracks this entity and it is still writable.
        Assert.False(entity.TryGet(out Collider? _));
        entity.Position = new Vector2(3e38f, 0f);
        Assert.Equal(new Vector2(3e38f, 0f), entity.Position);
    }

    [Fact]
    public void Components_AreQueriedByAssignableTypeWithoutAllocation()
    {
        TestEntity entity = new(Vector2.Zero);
        DerivedComponent component = new();
        entity.Add(component);

        Assert.True(entity.TryGet(out BaseComponent? found));
        Assert.Same(component, found);
        Assert.Same(component, entity.Get<DerivedComponent>());
        Assert.Throws<InvalidOperationException>(() => entity.Get<QuadRenderer>());
    }

    [Fact]
    public void ARemovedComponent_CanBeAttachedToAnotherEntity()
    {
        TestEntity first = new(Vector2.Zero);
        TestEntity second = new(Vector2.Zero);
        DerivedComponent component = new();
        first.Add(component);

        first.Remove(component);
        second.Add(component);

        Assert.False(first.TryGet<DerivedComponent>(out _));
        Assert.Same(component, second.Get<DerivedComponent>());
        Assert.Same(second, component.Entity);
    }

    [Fact]
    public void RemovingARenderer_InvalidatesTheScenesDrawOrder()
    {
        Scene scene = new();
        TestEntity entity = new(Vector2.Zero);
        QuadRenderer renderer = new(Vector2.One, ColorRgba.White);
        entity.Add(renderer);
        scene.Add(entity);
        SceneSimulation simulation = new(scene);
        Assert.Single(simulation.View.Quads.ToArray());

        entity.Remove(renderer);
        simulation.Step(SceneFixtures.Step());

        Assert.Empty(simulation.View.Quads.ToArray());
    }

    [Fact]
    public void AComponentRemovingItself_DoesNotSkipTheOneShiftedIntoItsSlot()
    {
        List<string> log = [];
        TestEntity entity = new(Vector2.Zero);
        SelfRemovingComponent removing = new(log);
        entity.Add(removing);
        entity.Add(new RecordingComponent(log));
        Scene scene = new();
        scene.Add(entity);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["remove", "next"], log);
    }

    private sealed class TestEntity(Vector2 position) : Entity(position);

    private abstract class BaseComponent : Component;

    private sealed class DerivedComponent : BaseComponent;

    private sealed class SelfRemovingComponent(List<string> log) : Component
    {
        public override void Update(in StepContext context)
        {
            log.Add("remove");
            Entity!.Remove(this);
        }
    }

    private sealed class RecordingComponent(List<string> log) : Component
    {
        public override void Update(in StepContext context) => log.Add("next");
    }
}
