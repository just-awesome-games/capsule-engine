using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class EntityTests
{
    private const SceneFixtures.Logs Lifecycle = SceneFixtures.Logs.Join | SceneFixtures.Logs.Start | SceneFixtures.Logs.Leave;

    [Fact]
    public void Teleport_ResetsBothEndsOfInterpolation()
    {
        Node entity = new(new Vector2(4, 6));
        entity.Position = new Vector2(8, 10);

        entity.Teleport(new Vector2(40, 50));

        Assert.Equal(new Vector2(40, 50), entity.Position);
        Assert.Equal(entity.Position, entity.PreviousTransform.Position);
    }

    [Fact]
    public void APositionThatIsNotFinite_IsRefusedAndLeavesTheEntityWhereItWas()
    {
        Node entity = new(new Vector2(4, 6));

        ArgumentOutOfRangeException nan = Assert.Throws<ArgumentOutOfRangeException>(() => entity.Position = new Vector2(float.NaN, 0f));
        Assert.Contains("normalizing a zero vector", nan.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => entity.Position = new Vector2(0f, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => entity.Teleport(new Vector2(float.NaN, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Node(new Vector2(float.NaN, 0f)));

        Assert.Equal(new Vector2(4, 6), entity.Position);
        Assert.Equal(new Vector2(4, 6), entity.PreviousTransform.Position);
    }

    [Fact]
    public void Components_AreQueriedByAssignableType()
    {
        Node entity = new(Vector2.Zero);
        DerivedComponent component = new();
        entity.Add(component);

        Assert.True(entity.TryGet(out BaseComponent? found));
        Assert.Same(component, found);
        Assert.Same(component, entity.Get<DerivedComponent>());
        Assert.True(entity.Has<BaseComponent>());
        Assert.False(entity.Has<SpriteRenderer>());
        Assert.Throws<InvalidOperationException>(() => entity.Get<SpriteRenderer>());
    }

    [Fact]
    public void TryGetInParent_PrefersSelfThenTheNearestAncestor()
    {
        Node root = new(Vector2.Zero);
        Node middle = new(Vector2.Zero) { Parent = root };
        Node leaf = new(Vector2.Zero) { Parent = middle };
        Node sibling = new(Vector2.Zero) { Parent = root };
        DerivedComponent rootComponent = new();
        DerivedComponent middleComponent = new();
        DerivedComponent leafComponent = new();
        root.Add(rootComponent);
        middle.Add(middleComponent);
        sibling.Add(new SceneFixtures.RecordingComponent("sibling", []));

        Assert.Same(middleComponent, leaf.GetInParent<BaseComponent>());

        leaf.Add(leafComponent);

        Assert.True(leaf.TryGetInParent(out DerivedComponent? found));
        Assert.Same(leafComponent, found);
        Assert.Same(rootComponent, root.GetInParent<DerivedComponent>());
        Assert.False(root.TryGetInParent<SceneFixtures.RecordingComponent>(out _));
        Assert.False(leaf.TryGetInParent<SceneFixtures.RecordingComponent>(out _));
    }

    // Entities climb as components do, by any type an entity is assignable to, an interface included.
    [Fact]
    public void FindInParent_PrefersSelfThenTheNearestAncestor()
    {
        MarkedEntity root = new();
        MarkedEntity middle = new() { Parent = root };
        Node leaf = new(Vector2.Zero) { Parent = middle };

        Assert.Same(middle, leaf.FindInParent<IMarked>());
        Assert.Same(root, root.FindInParent<MarkedEntity>());

        Node plain = new(Vector2.Zero);
        Node plainLeaf = new(Vector2.Zero) { Parent = plain };
        _ = new MarkedEntity { Parent = plain };
        _ = new MarkedEntity { Parent = plainLeaf };

        Assert.Null(plainLeaf.FindInParent<IMarked>());
    }

    [Fact]
    public void GetInParent_ThrowsWhenNoEntityInTheChainMatches()
    {
        Node root = new(Vector2.Zero);
        Node child = new(Vector2.Zero) { Parent = root };
        root.Add(new DerivedComponent());

        Assert.Throws<InvalidOperationException>(() => child.GetInParent<SpriteRenderer>());
    }

    [Fact]
    public void ARemovedComponent_CanBeAttachedToAnotherEntity()
    {
        Node first = new(Vector2.Zero);
        Node second = new(Vector2.Zero);
        DerivedComponent component = new();
        first.Add(component);

        first.Remove(component);
        second.Add(component);

        Assert.False(first.Has<DerivedComponent>());
        Assert.Same(component, second.Get<DerivedComponent>());
        Assert.Same(second, component.Entity);
    }

    [Fact]
    public void RemovingARenderer_InvalidatesTheScenesDrawOrder()
    {
        Scene scene = new();
        Node entity = new(Vector2.Zero);
        SpriteRenderer renderer = new(SceneFixtures.Frame(1, 1));
        entity.Add(renderer);
        scene.Add(entity);
        SceneSimulation simulation = new(scene);
        Assert.Single(simulation.View.Sprites.ToArray());

        entity.Remove(renderer);
        simulation.Step(SceneFixtures.Step());

        Assert.Empty(simulation.View.Sprites.ToArray());
    }

    [Fact]
    public void AComponentRemovingItself_DoesNotSkipTheOneShiftedIntoItsSlot()
    {
        List<string> log = [];
        Node entity = new(Vector2.Zero);
        SelfRemovingComponent removing = new(log);
        entity.Add(removing);
        entity.Add(new SceneFixtures.RecordingComponent("next", log));
        Scene scene = new();
        scene.Add(entity);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["remove", "next", "next.late"], log);
    }

    // A component brings its parts in OnAttached, and they join the scene before it does. Detaching it
    // takes them away after it has left the scene.
    [Fact]
    public void AComponentAttachedInAStartedScene_BringsItsPartsAndTakesThemWhenDetached()
    {
        List<string> log = [];
        Node entity = new(Vector2.Zero);
        Scene scene = new();
        scene.Add(entity);
        using SceneSimulation simulation = new(scene);
        Composite composite = new(log);

        entity.Add(composite);

        Assert.Equal(["attached", "part+", "part!", "composite+", "composite!"], log);
        Assert.Same(entity, composite.Part.Entity);

        log.Clear();
        entity.Remove(composite);

        Assert.Equal(["composite-", "detached", "part-"], log);
        Assert.Same(entity, composite.DetachedFrom);
        Assert.Null(composite.Part.Entity);
        Assert.Empty(entity.Components.ToArray());
    }

    // A component that detaches itself in OnAttached never joins the scene its entity is in.
    [Fact]
    public void AComponentDetachingItselfInOnAttached_NeverEntersTheScene()
    {
        List<string> log = [];
        Node entity = new(Vector2.Zero);
        Scene scene = new();
        scene.Add(entity);
        using SceneSimulation simulation = new(scene);
        SelfDetaching component = new(log);

        entity.Add(component);

        Assert.Equal(["attached", "detached"], log);
        Assert.Null(component.Entity);
    }

    // An entity whose root is leaving at this step's end does not start what is attached to it. The
    // component would otherwise start on an entity about to leave its scene.
    [Fact]
    public void AComponentAttachedBeneathARootRemovedThisStep_DoesNotStart()
    {
        List<string> log = [];
        Node root = new(Vector2.Zero);
        Node child = new(Vector2.Zero) { Parent = root };
        SceneFixtures.RecordingComponent late = new("late", log, Lifecycle);
        bool removing = false;
        SceneFixtures.HookScene scene = new(step: (Scene stepping, in StepContext _) =>
        {
            if (removing)
            {
                stepping.Remove(root);
                child.Add(late);
            }
        });
        scene.Add(root);
        using SceneSimulation simulation = new(scene);

        removing = true;
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["late+", "late-"], log);
    }

    // A throwing OnDetached still releases the component from the engine. The draw order no longer
    // holds a renderer whose entity is gone.
    [Fact]
    public void ARendererWhoseOnDetachedThrows_IsStillDroppedFromTheDrawOrder()
    {
        Scene scene = new();
        Node entity = new(Vector2.Zero);
        RefusingToLeave renderer = new();
        entity.Add(renderer);
        scene.Add(entity);
        SceneSimulation simulation = new(scene);
        Assert.Single(simulation.View.Sprites.ToArray());

        Assert.Throws<InvalidOperationException>(() => entity.Remove(renderer));
        simulation.Step(SceneFixtures.Step());

        Assert.Null(renderer.Entity);
        Assert.Empty(simulation.View.Sprites.ToArray());
    }

    [Fact]
    public void AComponentWhoseOnAttachedThrows_StaysUnattached()
    {
        Node entity = new(Vector2.Zero);
        Refusing refusing = new();

        Assert.Throws<InvalidOperationException>(() => entity.Add(refusing));

        Assert.Null(refusing.Entity);
        Assert.False(entity.Has<Refusing>());
    }

    private interface IMarked;

    private sealed class MarkedEntity() : Entity(Vector2.Zero), IMarked;

    private abstract class BaseComponent : Component;

    private sealed class DerivedComponent : BaseComponent;

    private sealed class Composite(List<string> log) : Component
    {
        internal SceneFixtures.RecordingComponent Part { get; } = new("part", log, Lifecycle);

        internal Entity? DetachedFrom { get; private set; }

        protected internal override void OnAttached(Entity entity)
        {
            log.Add("attached");
            entity.Add(Part);
        }

        protected internal override void OnDetached(Entity entity)
        {
            Assert.Null(Entity);
            log.Add("detached");
            DetachedFrom = entity;
            entity.Remove(Part);
        }

        protected internal override void OnAddedToScene() => log.Add("composite+");

        protected internal override void OnStart() => log.Add("composite!");

        protected internal override void OnRemovedFromScene() => log.Add("composite-");
    }

    private sealed class SelfDetaching(List<string> log) : Component
    {
        protected internal override void OnAttached(Entity entity)
        {
            log.Add("attached");
            entity.Remove(this);
        }

        protected internal override void OnDetached(Entity entity) => log.Add("detached");

        protected internal override void OnAddedToScene() => log.Add("entered");
    }

    private sealed class RefusingToLeave : Renderer
    {
        protected internal override void Draw(FrameView view) =>
            view.Add(new SpriteIntent(
                SceneFixtures.Frame(1, 1),
                Entity!.PreviousTransform.Position,
                Entity.Position,
                PreviousRotation: 0f,
                Rotation: 0f,
                Vector2.One,
                FlipX: false,
                FlipY: false,
                ColorRgba.White));

        protected internal override void OnDetached(Entity entity) =>
            throw new InvalidOperationException("Refused to leave.");
    }

    // Attaches a part before throwing. The rollback must find this component, not the last one.
    private sealed class Refusing : Component
    {
        protected internal override void OnAttached(Entity entity)
        {
            entity.Add(new DerivedComponent());
            throw new InvalidOperationException("Refused.");
        }
    }

    private sealed class SelfRemovingComponent(List<string> log) : Component
    {
        protected internal override void OnStep(in StepContext context)
        {
            log.Add("remove");
            Entity!.Remove(this);
        }
    }
}
