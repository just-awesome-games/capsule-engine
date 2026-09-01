using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Capsule.Collision;
using Capsule.Input;
using Capsule.Scenes;
using Xunit.Abstractions;

namespace Capsule.Tests.Performance;

[Collection(StagePerformanceCollection.Name)]
public sealed class CollisionPerformanceTests(ITestOutputHelper output)
{
    private const int WarmupSteps = 120;
    private const int MeasuredSteps = 600;
    private const int RaysPerBatch = 64;
    private const int OverlapsPerBatch = 64;
    private const int DiagonalCastsPerBatch = 4;

    // The same shape of claim the stage workload makes: the work measures in microseconds against
    // a 16.7 ms frame, so a ceiling this far above it trips on a collapse and not on drift.
    private static readonly TimeSpan MaxMeanStep = TimeSpan.FromMilliseconds(1);

    // The diagonal batch is four 4096 px sweeps a step, not microsecond work: 0.1 to 0.3 ms
    // uninstrumented on a desktop and some 2.3 ms under coverage instrumentation. A frame is the
    // nearest ceiling that still reads a collapse without tripping on instrumentation or on a
    // shared runner, and it is all a wall-clock number in either environment can honestly claim.
    private static readonly TimeSpan MaxDiagonalBatchStep = TimeSpan.FromMilliseconds(16);

    [Fact]
    public void AMoverOnARoomScaleTilemap_AllocatesNothingAndStaysWithinTheStepBudget()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.Filter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);
        Aabb2D box = CollisionWorkload.Mover;
        Contact2D[] contacts = new Contact2D[16];
        float direction = 1f;

        Report("mover on a room", Measure(step =>
        {
            MoveResult2D result = world.MoveBox(box, new Vector2(direction * 2f, 4f), filter, contacts);
            box = box.Translated(result.Translation);

            if (result.BlockedX)
            {
                direction = -direction;
            }

            return result.ContactCount + step;
        }), MaxMeanStep);
    }

    [Fact]
    public void ABatchOfRaysAndOverlaps_AllocatesNothing()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.Filter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);
        Contact2D[] contacts = new Contact2D[32];
        RayHit2D[] hits = new RayHit2D[16];

        Report("64 rays and 64 overlaps", Measure(step =>
        {
            int found = 0;
            for (int index = 0; index < RaysPerBatch; index++)
            {
                float x = ((index * 61) + step) % (CollisionWorkload.TilesWide * CollisionWorkload.TileSize);
                Vector2 origin = new(x, 34f * CollisionWorkload.TileSize);

                if (world.Raycast(origin, Vector2.UnitY, 256f, filter, out RayHit2D hit))
                {
                    found += hit.Target.CellY;
                }

                found += world.RaycastAll(origin, Vector2.UnitX, 128f, filter, hits);
            }

            for (int index = 0; index < OverlapsPerBatch; index++)
            {
                float x = ((index * 37) + step) % (CollisionWorkload.TilesWide * CollisionWorkload.TileSize);
                found += world.OverlapBox(
                    Aabb2D.FromCorner(new Vector2(x, 37f * CollisionWorkload.TileSize), new Vector2(24f, 24f)),
                    filter,
                    contacts);
            }

            return found;
        }), MaxMeanStep);
    }

    // Four sweeps across the map corner to corner, the longest casts a room-scale game issues. What
    // this asserts is the frame ceiling and zero allocation; whether a sweep walks the band its
    // shape covers rather than the rectangle its bounds describe is a cell count, not a duration,
    // and is not claimed here.
    [Fact]
    public void ABatchOfMapLengthDiagonalCasts_AllocatesNothingAndStaysWithinTheStepBudget()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.Filter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);
        Shape2D shape = Shape2D.Box(Vector2.Zero, new Vector2(12f, 24f));

        const float across = CollisionWorkload.TilesWide * CollisionWorkload.TileSize;
        const float down = CollisionWorkload.TilesHigh * CollisionWorkload.TileSize;

        Report("4 map-length diagonal casts", Measure(step =>
        {
            int found = 0;
            for (int index = 0; index < DiagonalCastsPerBatch; index++)
            {
                float offset = ((index * 13) + step) % CollisionWorkload.TileSize;
                Vector2 origin = new(offset, offset);

                if (world.ShapeCast(shape, origin, new Vector2(across, down), filter, out ShapeCastHit2D hit))
                {
                    found += hit.Target.CellX;
                }
            }

            return found;
        }), MaxDiagonalBatchStep);
    }

    // A RaycastAll whose span is full has the same reach left as the Raycast that took one hit, so
    // it must stop in the same place. Left to run to the grid's far edge it would cell-test the
    // rest of the map for results it has already decided it cannot keep — measurable here as a
    // multiple of the bounded cast rather than a near-equal to it.
    [Fact]
    public void ASaturatedRaycastAll_StopsWhereItsSpanFillsRatherThanWalkingOnToTheGridsEdge()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.Filter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);
        RayHit2D[] one = new RayHit2D[1];

        // Along the floor: every cell of the row is solid, so an unpruned walk tests all 256 of
        // them after the very first has already filled the span.
        Vector2 origin = new(8f, (41.5f * CollisionWorkload.TileSize) + 0.5f);
        const float across = CollisionWorkload.TilesWide * CollisionWorkload.TileSize;

        TimeSpan bounded = Measure(_ => world.Raycast(origin, Vector2.UnitX, across, filter, out RayHit2D _) ? 1 : 0).Elapsed;
        TimeSpan saturated = Measure(_ => world.RaycastAll(origin, Vector2.UnitX, across, filter, one)).Elapsed;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"nearest {bounded.TotalMilliseconds * 1000.0 / MeasuredSteps:0.00} us, saturated {saturated.TotalMilliseconds * 1000.0 / MeasuredSteps:0.00} us"));

        Assert.True(
            saturated < bounded * 5,
            FormattableString.Invariant(
                $"a saturated RaycastAll took {saturated.TotalMilliseconds:0.000} ms against the nearest cast's {bounded.TotalMilliseconds:0.000} ms, so it is still walking the grid past its limit."));
    }

    [Fact]
    public void AColliderWalkingASceneWithContactEvents_AllocatesNothingPerStep()
    {
        Scene scene = CollisionWorkload.Room();
        CollisionWorkload.Walker walker = new(CollisionWorkload.Mover.Min);
        scene.Add(walker);

        using SceneSimulation simulation = new(scene, null, StageWorkload.Defaults);

        // One input state for the run: building one a step is the harness allocating, not the step.
        InputState input = new(new ActionBindings());

        Report("walker in a scene", Measure(step =>
        {
            simulation.Step(new StepContext(StageWorkload.StepSeconds, input, step));
            return walker.Contacts;
        }), MaxMeanStep);
    }

    private static (long Bytes, TimeSpan Elapsed, long Guard) Measure(Func<int, int> step)
    {
        long guard = 0;
        for (int index = 0; index < WarmupSteps; index++)
        {
            guard += step(index);
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();

        for (int index = 0; index < MeasuredSteps; index++)
        {
            guard += step(WarmupSteps + index);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        return (GC.GetAllocatedBytesForCurrentThread() - bytes, elapsed, guard);
    }

    private void Report(string label, (long Bytes, TimeSpan Elapsed, long Guard) measured, TimeSpan maxMeanStep)
    {
        (long bytes, TimeSpan elapsed, long guard) = measured;
        TimeSpan mean = elapsed / MeasuredSteps;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: {bytes} bytes over {MeasuredSteps} steps, mean {mean.TotalMilliseconds * 1000.0:0.00} us (guard {guard})"));

        Assert.Equal(0, bytes);
        Assert.True(
            mean < maxMeanStep,
            FormattableString.Invariant($"{label} averaged {mean.TotalMilliseconds:0.000} ms a step."));
    }
}
