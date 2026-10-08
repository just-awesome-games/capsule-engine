using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;
using Recorder = Capsule.Tests.Scenes.SceneFixtures.Recorder;
using Watcher = Capsule.Tests.Scenes.SceneFixtures.Watcher;

namespace Capsule.Tests.Scenes;

public sealed class EntityReparentTests
{
    private const float Tolerance = 1e-3f;

    [Fact]
    public void AParentWrite_KeepsTheSubtreeHeld_WithoutHooksRestartOrPoolReturn()
    {
        List<string> log = [];
        EntityPool<Recorder> pool = new(() => new Recorder("log", log, logsStart: true), capacity: 1);
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, new Vector2(1000f, 1000f)));
        SceneFixtures.Drifter axe = new(new Vector2(0f, 100f));
        Recorder thrown = pool.Take();
        thrown.Position = new Vector2(10f, 0f);
        thrown.Parent = axe;
        thrown.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));
        Recorder grandchild = new("grandchild", log, logsStart: true) { Parent = thrown };
        int steps = 0;
        scene.Add(axe);
        scene.Add(new Watcher(_ =>
        {
            if (++steps == 2)
            {
                thrown.Parent = null;
            }
        }));

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        log.Clear();
        simulation.Step(SceneFixtures.Step(tick: 1));

        Assert.Equal(["log", "grandchild", "log.late", "grandchild.late"], log);
        Assert.Same(scene, thrown.Scene);
        Assert.Equal(0, pool.Available);
        Assert.Null(thrown.Parent);

        SpriteIntent intent = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(new Vector2(12f, 100f), intent.Position);
    }

    // A launch happens on the step its owner is removed. The write takes the part out of that removal.
    [Fact]
    public void AChildWrittenOutAfterItsOwnerIsRemoved_StaysHeld_AtTheEndOfTheRoots()
    {
        EntityPool<Node> pool = new(() => new Node(Vector2.Zero), capacity: 1);
        SceneFixtures.HookScene scene = new();
        Node owner = new(Vector2.Zero);
        Node part = pool.Take();
        part.Parent = owner;
        Node bystander = new(Vector2.Zero);
        scene.Add(owner);
        scene.Add(bystander);
        Watcher watcher = new(_ =>
        {
            scene.Remove(owner);
            Assert.True(part.IsRemovalPending);
            part.Parent = null;
            Assert.False(part.IsRemovalPending);
        });
        scene.Add(watcher);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Null(owner.SceneOrNull);
        Assert.Equal([bystander, watcher, part], scene.Entities.ToArray());
        Assert.Equal(0, pool.Available);
    }

    [Fact]
    public void AParentWrite_KeepsTheWorldTransform_AndRecomputesTheLocal_UnderAnyParentOrNone()
    {
        ColorRgba red = new(255, 0, 0);
        Node home = new(new Vector2(10f, 5f)) { Rotation = 0.3f, Scale = new Vector2(2f, 0.5f) };
        Node dest = new(new Vector2(-30f, 8f)) { Rotation = -0.6f, Scale = new Vector2(-1.5f, 3f), Tint = red, ZIndex = 5 };
        Node other = new(new Vector2(7f, -3f)) { Rotation = 1.1f, Scale = new Vector2(0.5f, 4f) };
        Node mover = new(new Vector2(4f, 2f)) { Rotation = 0.2f, Scale = new Vector2(3f, 1.5f), Parent = home };
        mover.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));
        Node sibling = new(Vector2.Zero);
        sibling.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));
        Transform2D world = mover.WorldTransform;
        Transform2D local = mover.Transform;
        int steps = 0;

        void Reparent(Entity? parent)
        {
            mover.Parent = parent;

            Assert.Same(parent, mover.Parent);
            AssertNear(Parts(world), Parts(mover.WorldTransform));
            Assert.NotEqual(local, mover.Transform);
            local = mover.Transform;
        }

        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, new Vector2(1000f, 1000f)));
        scene.Add(home);
        scene.Add(dest);
        scene.Add(other);
        scene.Add(sibling);
        scene.Add(new Watcher(_ =>
        {
            if (++steps == 1)
            {
                Reparent(dest);
                Assert.Equal([mover], dest.Children.ToArray());
                Assert.True(home.Children.IsEmpty);
            }
            else
            {
                Reparent(other);
                Reparent(null);
                Assert.Equal(mover.WorldTransform, mover.Transform);
            }
        }));

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        // The mover now sums the destination's band, so it draws after the sibling it preceded.
        SpriteIntent[] drawn = simulation.View.Sprites.ToArray();
        Assert.Equal([ColorRgba.White, red], drawn.Select(static intent => intent.Color));
        simulation.Step(SceneFixtures.Step(tick: 1));
    }

    [Fact]
    public void ACarrierReparentedMidStep_DoesNotMoveItsRider_AndKeepsCarryingIt()
    {
        const string Platform = "platform";
        Node holder = new(new Vector2(100f, 0f));
        Entity carrier = new(holder, new Vector2(0f, 40f));
        carrier.Add(new BoxCollider2D(new Vector2(32f, 8f)) { Layer = Platform });
        Node rider = new(new Vector2(112f, 20f));
        BoxCollider2D feet = new(new Vector2(8f, 8f));
        rider.Add(feet);
        KinematicBody2D body = new(feet) { BlockedBy = new(Platform), MovedBy = new(Platform) };
        rider.Add(body);
        SceneFixtures.HookScene scene = new();
        using SceneSimulation simulation = new(scene);
        scene.Add(holder);
        scene.Add(rider);
        body.Move(new Vector2(0f, 40f));
        Assert.True(body.IsOnFloor);
        Vector2 landed = rider.Position;
        scene.Add(new Watcher(_ => carrier.Parent = null));

        simulation.Step(SceneFixtures.Step());

        Assert.Null(carrier.Parent);
        Assert.Equal(landed, rider.Position);
        carrier.Position += new Vector2(2f, 0f);
        Assert.Equal(landed.X + 2f, rider.Position.X, Tolerance);
    }

    [Fact]
    public void AMovingEntityReparentedMidStep_DrawsTheSameInterpolation_AsOneLeftAlone()
    {
        SpriteIntent left = DrawAfterSecondStep(reparent: false);
        SpriteIntent moved = DrawAfterSecondStep(reparent: true);

        Assert.NotEqual(left.PreviousPosition, left.Position);
        AssertNear(Parts(left), Parts(moved));
    }

    private static SpriteIntent DrawAfterSecondStep(bool reparent)
    {
        Node home = new(new Vector2(10f, 5f)) { Rotation = 0.3f, Scale = new Vector2(2f, 0.5f) };
        Node dest = new(new Vector2(-30f, 8f)) { Rotation = -0.6f, Scale = new Vector2(1.5f, 3f) };
        SceneFixtures.Drifter drifter = new(new Vector2(4f, 2f)) { Rotation = 0.2f, Parent = home };
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(1, 1)));
        int steps = 0;
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, new Vector2(1000f, 1000f)));
        scene.Add(home);
        scene.Add(dest);
        scene.Add(new Watcher(_ =>
        {
            if (reparent && ++steps == 2)
            {
                drifter.Parent = dest;
            }
        }));

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        simulation.Step(SceneFixtures.Step(tick: 1));

        return Assert.Single(simulation.View.Sprites.ToArray());
    }

    // A moved subtree keeps stepping in its old place until the drain lays step order out.
    [Fact]
    public void AStepOrder_FollowsTheTree_AfterWritesAddsAndRemovesInOneStep()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new(start: SceneFixtures.Opens(Vector2.Zero, new Vector2(100f)));
        Recorder r1 = new("r1", log);
        Recorder a1 = new("a1", log) { Parent = r1 };
        Recorder a1c = new("a1c", log) { Parent = a1 };
        Recorder a2 = new("a2", log) { Parent = r1 };
        Recorder a3 = new("a3", log) { Parent = r1 };
        Recorder r2 = new("r2", log);
        Recorder b1 = new("b1", log) { Parent = r2 };
        Recorder r3 = new("r3", log);
        Recorder joined = new("joined", log);
        Recorder lateRoot = new("lateRoot", log);
        Recorder queued = new("queued", log);
        Node mover = new(Vector2.Zero);
        VisibleOnScreenNotifier2D notifier = new() { Rect = new Rect(Vector2.Zero, new Vector2(4f)) };
        mover.Add(notifier);
        notifier.ScreenEntered += () => mover.Parent = r2;
        bool done = false;
        Watcher watcher = new(_ =>
        {
            if (done)
            {
                return;
            }

            done = true;
            a1.Parent = r3;
            a1.Parent = r2;
            a2.Parent = null;
            a1c.Parent = null;
            a2.Parent = r3;
            a2.Parent = null;
            joined.Parent = r2;
            a3.Parent = r2;
            scene.Add(lateRoot);
            scene.Add(queued);
            queued.Parent = r3;
            mover.Parent = r3;
            scene.Remove(b1);
        });
        scene.Add(watcher);
        scene.Add(r1);
        scene.Add(r2);
        scene.Add(r3);

        using SceneSimulation simulation = new(scene);
        log.Clear();
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(["r1", "a1", "a1c", "a2", "a3", "r2", "b1", "r3"], Steps(log));
        Assert.Equal(
            [watcher, r1, r2, a1, joined, a3, mover, r3, queued, a1c, a2, lateRoot],
            scene.Entities.ToArray());

        log.Clear();
        simulation.Step(SceneFixtures.Step(tick: 1));
        Assert.Equal(["r1", "r2", "a1", "joined", "a3", "r3", "queued", "a1c", "a2", "lateRoot"], Steps(log));
    }

    [Fact]
    public void AChildThatLeavesItsParentDuringItsJoin_DoesNotKeepItsNextSiblingOut()
    {
        SceneFixtures.HookScene scene = new SceneFixtures.HookScene().Started();
        Node root = new(Vector2.Zero);
        Entity leaver = null!;
        leaver = new SceneFixtures.Meddler(_ => leaver.Parent = null) { Parent = root };
        Entity stayer = new(root);

        scene.Add(root);

        Assert.Same(scene, stayer.SceneOrNull);
        Assert.Equal([root, stayer, leaver], scene.Entities.ToArray());
    }

    [Fact]
    public void AReparentBetweenStepModes_KeepsTheOldHoldForTheStep_AndTakesTheNewOneFromTheNext()
    {
        List<string> log = [];
        SceneFixtures.HookScene scene = new() { Paused = true };
        Node whenPaused = new(Vector2.Zero) { StepMode = StepMode.WhenPaused };
        Node pausable = new(Vector2.Zero) { StepMode = StepMode.Pausable };
        Recorder mover = new("mover", log) { StepMode = StepMode.Inherit, Parent = whenPaused };
        scene.Add(new Watcher(_ => mover.Parent = pausable) { StepMode = StepMode.Always });
        scene.Add(whenPaused);
        scene.Add(pausable);

        using SceneSimulation simulation = new(scene);
        log.Clear();
        simulation.Step(SceneFixtures.Step());
        Assert.Equal(["mover", "mover.late"], log);

        log.Clear();
        simulation.Step(SceneFixtures.Step(tick: 1));
        Assert.Empty(log);
    }

    private static float[] Parts(Transform2D t) => [t.Position.X, t.Position.Y, t.Rotation, t.Scale.X, t.Scale.Y];

    private static float[] Parts(SpriteIntent i) =>
        [i.PreviousPosition.X, i.PreviousPosition.Y, i.Position.X, i.Position.Y, i.PreviousRotation, i.Rotation, i.Size.X, i.Size.Y];

    private static void AssertNear(float[] expected, float[] actual) =>
        Assert.Equal(expected, actual, static (a, b) => MathF.Abs(a - b) < Tolerance);

    // The step records alone, without hooks and late steps.
    private static string[] Steps(List<string> log) =>
        [.. log.Where(static entry => !entry.EndsWith(".late") && !entry.EndsWith('+') && !entry.EndsWith('-') && !entry.EndsWith('!'))];
}
