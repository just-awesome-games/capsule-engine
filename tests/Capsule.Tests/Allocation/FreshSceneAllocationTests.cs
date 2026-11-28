using System.Numerics;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Physics;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Allocation;

// The first use of collision scratch in a scene or world built mid-run. A game builds a fresh scene for
// every room, and no warm-up reaches these first uses. Each test runs the same work once beforehand,
// which keeps compiling the code out of the count.
[Collection(StageAllocationCollection.Name)]
public sealed class FreshSceneAllocationTests(ITestOutputHelper output)
{
    private const int TrainLength = 40;

    // Longer than a push. A shoved body reaches into the next one and never past it.
    private const float CarLength = 24f;

    // A lift joins a running scene beside a resting crate, and shoves the crate on its first stroke. The
    // scene's other lift has been shoving its own crate all along.
    [Fact]
    public void APusherJoiningARunningScene_ShovesWithoutAllocating()
    {
        Scene scene = CollisionWorkload.Room();
        scene.Add(new CollisionWorkload.Lift(new Vector2(96f, 632f)));
        scene.Add(new CollisionWorkload.Hauled(new Vector2(140f, 620f), walk: -1f));
        CollisionWorkload.Hauled crate = scene.Add(new CollisionWorkload.Hauled(new Vector2(1040f, 620f), walk: 0f));
        CollisionWorkload.Lift lift = new(new Vector2(996f, 632f));

        long bytes = BytesAfterJoining(scene, lift);

        StepMeasurement.Report(output, "a lift's first shoves", bytes);
        Assert.True(crate.Position.X > 1040f, $"the crate stayed at {crate.Position.X}");
        Assert.True(crate.Position.X > lift.Position.X);
    }

    // A lift joins a running scene under a falling body, which lands on it and rides. Every collider holds room for its
    // riders from construction. A first ride then costs nothing in the step it lands in.
    [Fact]
    public void ABodyFirstRidingAColliderJoiningARunningScene_RidesWithoutAllocating()
    {
        Scene scene = CollisionWorkload.Room();
        scene.Add(new CollisionWorkload.Lift(new Vector2(96f, 632f)));
        scene.Add(new CollisionWorkload.Hauled(new Vector2(96f, 620f), walk: 0f));
        CollisionWorkload.Hauled rider = new(new Vector2(996f, 620f), walk: 0f);

        long bytes = BytesAfterJoining(scene, new CollisionWorkload.Lift(new Vector2(996f, 632f)), rider);

        StepMeasurement.Report(output, "a first ride on a collider joining mid-run", bytes);
        Assert.True(rider.Position.X != 996f, $"the body never rode the lift, and stayed at {rider.Position.X}");
    }

    // A reporter's sorted candidates name the slot after it. A collider in the slot before it then
    // arrives, which a fresh world merges in ahead of the sorted entry.
    [Fact]
    public void AFreshWorldsFirstOutOfOrderCandidate_MergesWithoutAllocating()
    {
        Contact2D[] contacts = new Contact2D[4];
        _ = SettleArrival(MergeWorld(out ColliderHandle warmEarly, out ColliderHandle warmReporter), warmEarly, warmReporter, contacts);

        CollisionWorld2D world = MergeWorld(out ColliderHandle early, out ColliderHandle reporter);
        long before = StepMeasurement.Start();
        int found = SettleArrival(world, early, reporter, contacts);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, bytes);
        Assert.Equal(2, found);
        Assert.Equal(early, contacts[0].Target.Collider);
    }

    // A pusher drives into the ends of two trains of bodies, one above the other. Each body is moved by
    // the layer of the one ahead of it, and passes through the layer of the one behind it. Its own move
    // then drives into the next body, and that body's shove runs inside its own. The upper train's
    // nesting runs deeper than the scene's shove buffer starts, before the pusher reaches the lower
    // train in its own gather. The first push grows the buffer once, and the second costs nothing.
    [Fact]
    public void AShoveDownATrainDeeperThanTheBuffer_MovesEveryBody_AndGrowsTheBufferOnce()
    {
        Scene scene = new Scene().Started();
        Slab pusher = scene.Add(new Slab(new Vector2(76f, 20f), CollisionFixtures.Platform, new Vector2(8f, 28f)));
        Rider[,] trains = new Rider[TrainLength, 2];
        for (int index = 0; index < TrainLength; index++)
        {
            for (int row = 0; row < 2; row++)
            {
                string movedBy = index == 0 ? CollisionFixtures.Platform : Car(index - 1);
                Vector2 corner = new(50f - (index * CarLength), 22f + (row * 14f));
                trains[index, row] = scene.Add(new Rider(corner, new Vector2(CarLength, Rider.Edge), movedBy)
                {
                    Collider = { Layer = Car(index) },
                    Mover = { BlockedBy = new(CollisionFixtures.Solid) },
                });
            }
        }

        pusher.Position += new Vector2(-20f, 0f);
        AssertTrainsAt(trains, 32f);

        long before = StepMeasurement.Start();
        pusher.Position += new Vector2(-20f, 0f);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, bytes);
        AssertTrainsAt(trains, 12f);
    }

    // Steps scene through the warm-up, adds joining to it, and returns the bytes of the measured steps after.
    private static long BytesAfterJoining(Scene scene, params Entity[] joining)
    {
        using SceneSimulation simulation = new(scene, run: StageWorkload.Defaults);
        InputState input = new(new ActionBindings());
        for (int step = 0; step < StepMeasurement.WarmupSteps; step++)
        {
            simulation.Step(new StepContext(StepMeasurement.StepSeconds, input, step));
        }

        foreach (Entity entity in joining)
        {
            scene.Add(entity);
        }

        return StepMeasurement.Bytes(
            step => simulation.Step(new StepContext(StepMeasurement.StepSeconds, input, StepMeasurement.WarmupSteps + step)),
            warmup: 0);
    }

    private static void AssertTrainsAt(Rider[,] trains, float lead)
    {
        for (int index = 0; index < TrainLength; index++)
        {
            for (int row = 0; row < 2; row++)
            {
                Assert.Equal(lead - (index * CarLength), trains[index, row].Position.X, CollisionFixtures.Tolerance);
            }
        }
    }

    // Three layers in turn along the train. A body's filter holds the layer behind it and not the one
    // ahead of it.
    private static string Car(int index) => FormattableString.Invariant($"car{index % 3}");

    // Three boxes in a row: the reporter in the middle slot, a box in the last slot touching it, and a
    // box in the first slot parked far off.
    private static CollisionWorld2D MergeWorld(out ColliderHandle early, out ColliderHandle reporter)
    {
        CollisionWorld2D world = new();
        CollisionLayer layer = world.Layer(CollisionWorld2D.DefaultLayerName);
        Shape2D box = Shape2D.Box(Vector2.Zero, new Vector2(8f, 8f));
        early = world.Add(box, new Vector2(1000f, 0f), layer);
        reporter = world.Add(box, Vector2.Zero, layer);
        world.SetContactFilter(reporter, CollisionFilter.Of(layer));
        _ = world.Add(box, new Vector2(8f, 0f), layer);

        Contact2D[] settled = new Contact2D[4];
        Assert.Equal(1, world.ContactsOf(reporter, settled));
        return world;
    }

    // Brings the far box flush against the reporter's other side and settles the reporter's contacts.
    private static int SettleArrival(CollisionWorld2D world, ColliderHandle early, ColliderHandle reporter, Span<Contact2D> contacts)
    {
        world.SetPosition(early, new Vector2(-8f, 0f));
        return world.ContactsOf(reporter, contacts);
    }
}
