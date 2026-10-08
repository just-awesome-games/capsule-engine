using System.Numerics;
using Capsule.Scenes;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;
using Watcher = Capsule.Tests.Scenes.SceneFixtures.Watcher;

namespace Capsule.Tests.Scenes;

public sealed class EntityReparentRefusalTests
{
    public enum Refusal
    {
        AnotherScene,
        NoScene,
        QueuedParent,
        OwnRemoval,
        ParentRemoval,
        AncestorRemoval,
        ZeroScale,
        NonFiniteLocal,
        LandingRemoval,
        LandingParent,
        NonFiniteRoot,
        QueuedAncestor,
        OwnHook,
        OwnComponentHook,
    }

    [Theory]
    [InlineData(Refusal.AnotherScene)]
    [InlineData(Refusal.NoScene)]
    [InlineData(Refusal.QueuedParent)]
    [InlineData(Refusal.OwnRemoval)]
    [InlineData(Refusal.ParentRemoval)]
    [InlineData(Refusal.AncestorRemoval)]
    [InlineData(Refusal.ZeroScale)]
    [InlineData(Refusal.NonFiniteLocal)]
    [InlineData(Refusal.LandingRemoval)]
    [InlineData(Refusal.LandingRemoval, true)]
    [InlineData(Refusal.LandingParent)]
    [InlineData(Refusal.LandingParent, true)]
    [InlineData(Refusal.NonFiniteRoot)]
    [InlineData(Refusal.QueuedAncestor)]
    [InlineData(Refusal.OwnHook)]
    [InlineData(Refusal.OwnHook, true)]
    [InlineData(Refusal.OwnComponentHook)]
    [InlineData(Refusal.OwnComponentHook, true)]
    public void AParentWriteInAScene_IsRefused_ForATargetTheSceneDoesNotHold_ARemovalPending_OrAPlacementItCannotKeep(Refusal refusal, bool outsideStep = false)
    {
        SceneFixtures.HookScene scene = new();
        Node home = new(new Vector2(1e10f, 0f));
        Entity mover = new(home);
        Node target = new(Vector2.Zero);
        Entity deep = new(target);
        Node flat = new(Vector2.Zero) { Scale = new Vector2(0f, 1f) };
        Node tiny = new(Vector2.Zero) { Scale = new Vector2(1e-30f, 1f) };
        Node huge = new(Vector2.Zero) { Scale = new Vector2(1e30f) };
        Node overflowing = new(Vector2.Zero) { Scale = new Vector2(1e30f), Parent = huge };
        Node queuedRoot = new(Vector2.Zero);
        Node queuedKid = new(Vector2.Zero) { Parent = queuedRoot };
        Node outsider = new(Vector2.Zero);
        bool hooked = false;
        Selfish self = new();
        self.OnLeave = () =>
        {
            Assert.Throws<InvalidOperationException>(() => self.Parent = target);
            hooked = true;
        };
        if (refusal == Refusal.OwnComponentHook)
        {
            self.Add(new Cleanup(self.OnLeave));
            self.OnLeave = null;
        }

        _ = new Leaver(home, () =>
        {
            if (refusal == Refusal.LandingRemoval)
            {
                Assert.Throws<InvalidOperationException>(() => mover.Parent = null);
                hooked = true;
            }
            else if (refusal == Refusal.LandingParent)
            {
                Assert.Throws<InvalidOperationException>(() => outsider.Parent = home);
                hooked = true;
            }
        });
        scene.Add(home);
        scene.Add(target);
        scene.Add(flat);
        scene.Add(tiny);
        scene.Add(huge);
        scene.Add(outsider);
        if (refusal is Refusal.OwnHook or Refusal.OwnComponentHook)
        {
            scene.Add(self);
        }

        SceneFixtures.HookScene elsewhere = new();
        Node stranger = new(Vector2.Zero);
        elsewhere.Add(stranger);
        Node loose = new(Vector2.Zero);

        scene.Add(new Watcher(_ =>
        {
            if (refusal == Refusal.OwnRemoval)
            {
                scene.Remove(mover);
            }
            else if (refusal is Refusal.ParentRemoval or Refusal.AncestorRemoval)
            {
                scene.Remove(target);
            }
            else if (refusal == Refusal.QueuedParent)
            {
                scene.Add(loose);
            }
            else if (refusal == Refusal.QueuedAncestor)
            {
                scene.Add(queuedRoot);
            }
            else if (refusal is Refusal.LandingRemoval or Refusal.LandingParent or Refusal.OwnHook or Refusal.OwnComponentHook)
            {
                if (!outsideStep)
                {
                    scene.Remove(refusal is Refusal.OwnHook or Refusal.OwnComponentHook ? self : home);
                }

                return;
            }

            Entity? attempt = refusal switch
            {
                Refusal.AnotherScene => stranger,
                Refusal.NoScene or Refusal.QueuedParent => loose,
                Refusal.ParentRemoval => target,
                Refusal.ZeroScale => flat,
                Refusal.NonFiniteLocal => tiny,
                Refusal.AncestorRemoval => deep,
                _ => null,
            };

            Entity subject = refusal switch
            {
                Refusal.NonFiniteRoot => overflowing,
                Refusal.QueuedAncestor => queuedKid,
                _ => mover,
            };

            Assert.Throws<InvalidOperationException>(() => subject.Parent = attempt);
        }));

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        if (outsideStep)
        {
            scene.Remove(refusal is Refusal.OwnHook or Refusal.OwnComponentHook ? self : home);
        }

        Assert.Same(refusal == Refusal.OwnRemoval ? null : home, mover.Parent);
        Assert.Same(huge, overflowing.Parent);
        Assert.Same(queuedRoot, queuedKid.Parent);
        Assert.Null(outsider.Parent);
        Assert.Null(self.Parent);
        Assert.Equal(refusal is Refusal.LandingRemoval or Refusal.LandingParent or Refusal.OwnHook or Refusal.OwnComponentHook, hooked);
    }

    // A removal hook can remove another root while the outer removal is still landing.
    [Fact]
    public void AParentWrite_IsRefusedForAChildOfARemovalStillLanding_WhileANestedRemovalLands()
    {
        SceneFixtures.HookScene scene = new();
        Node outer = new(Vector2.Zero);
        Node inner = new(Vector2.Zero);
        Entity child = new(outer);
        bool refused = false;
        _ = new Leaver(inner, () =>
        {
            Assert.Throws<InvalidOperationException>(() => child.Parent = null);
            refused = true;
        });
        _ = new Leaver(outer, () => scene.Remove(inner));
        scene.Add(outer);
        scene.Add(inner);

        scene.Remove(outer);

        Assert.True(refused);
        Assert.Same(outer, child.Parent);
    }

    private sealed class Selfish() : Entity(Vector2.Zero)
    {
        internal Action? OnLeave { get; set; }

        protected internal override void OnRemovedFromScene() => OnLeave?.Invoke();
    }

    private sealed class Cleanup(Action onRemoved) : Component
    {
        protected internal override void OnRemovedFromScene() => onRemoved();
    }

    private sealed class Leaver(Entity parent, Action onRemoved) : Entity(parent)
    {
        protected internal override void OnRemovedFromScene() => onRemoved();
    }
}
