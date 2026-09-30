using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Capsule.Input;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Physics;
using Capsule.Tests.Scenes;
using Capsule.Tiles;
using Xunit.Abstractions;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class CollisionAllocationTests(ITestOutputHelper output)
{
    private const int WarmupSteps = 120;
    private const int MeasuredSteps = 600;
    private const int RaysPerBatch = 64;
    private const int OverlapsPerBatch = 64;
    private const int DiagonalCastsPerBatch = 4;

    [Fact]
    public void AMoverOnARoomScaleTilemap_AllocatesNothing()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.CreateFilter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);
        Aabb2D box = CollisionWorkload.Mover;
        Contact2D[] contacts = new Contact2D[16];
        float direction = 1f;

        Report("mover on a room", Measure(step =>
        {
            MoveResult2D result = world.MoveBox(box, new Vector2(direction * 2f, 4f), filter, contacts);
            box = box.Translated(result.Translation);

            if (MathF.Abs(result.Translation.X) < 1f)
            {
                direction = -direction;
            }

            return result.ContactCount + step;
        }));
    }

    [Fact]
    public void ABatchOfRaysAndOverlaps_AllocatesNothing()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.CreateFilter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);
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
                found += world.OverlapBoxAll(
                    Aabb2D.FromCorner(new Vector2(x, 37f * CollisionWorkload.TileSize), new Vector2(24f, 24f)),
                    filter,
                    contacts);
            }

            return found;
        }));
    }

    // Two live worlds take turns with one mask, and with an empty one. After each world's first
    // resolve, a masked query costs what a filtered one does.
    [Fact]
    public void QueriesByOneMaskInTwoWorlds_AllocateNothingAfterEachWorldsFirstResolve()
    {
        CollisionWorld2D[] worlds = [CollisionWorkload.World(), CollisionWorkload.World()];
        CollisionMask mask = new(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);
        CollisionMask empty = new();
        Contact2D[] contacts = new Contact2D[32];

        Report("masked rays and overlaps in two worlds", Measure(step =>
        {
            CollisionWorld2D world = worlds[step & 1];
            float x = (step * 61) % (CollisionWorkload.TilesWide * CollisionWorkload.TileSize);
            Aabb2D box = Aabb2D.FromCorner(new Vector2(x, 37f * CollisionWorkload.TileSize), new Vector2(24f, 24f));
            int found = world.OverlapBoxAll(box, mask, contacts) + world.OverlapBoxAll(box, empty, contacts);

            return world.Raycast(new Vector2(x, 34f * CollisionWorkload.TileSize), Vector2.UnitY, 256f, mask, out RayHit2D hit)
                ? found + hit.Target.CellY
                : found;
        }));
    }

    // Four sweeps across the map corner to corner, the longest casts a room-scale game issues. What
    // this asserts is zero allocation; whether a sweep walks the band its shape covers rather than
    // the rectangle its bounds describe is a cell count, not a duration, and is not claimed here.
    [Fact]
    public void ABatchOfMapLengthDiagonalCasts_AllocatesNothing()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.CreateFilter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);
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
        }));
    }

    // A RaycastAll whose span is full has the same reach left as the Raycast that took one hit, so
    // it must stop in the same place. Left to run to the grid's far edge it would cell-test the
    // rest of the map for results it has already decided it cannot keep — counted in cells
    // reached, which reads the same on every machine.
    [Fact]
    public void ASaturatedRaycastAll_StopsWhereItsSpanFillsRatherThanWalkingOnToTheGridsEdge()
    {
        CollisionWorld2D world = CollisionWorkload.World();
        CollisionFilter filter = world.CreateFilter(CollisionWorkload.Solid, CollisionWorkload.Platform, CollisionWorkload.Actor);
        RayHit2D[] one = new RayHit2D[1];

        // Along the floor: every cell of the row is solid, so an unpruned walk reaches every one of
        // them after the very first has already filled the span.
        Vector2 origin = new(8f, (41.5f * CollisionWorkload.TileSize) + 0.5f);
        const float across = CollisionWorkload.TilesWide * CollisionWorkload.TileSize;

        world.ResetDiagnostics();
        Assert.True(world.Raycast(origin, Vector2.UnitX, across, filter, out RayHit2D _));
        long bounded = world.GridCellsTested;

        world.ResetDiagnostics();
        Assert.Equal(1, world.RaycastAll(origin, Vector2.UnitX, across, filter, one));
        long saturated = world.GridCellsTested;

        Assert.True(
            saturated <= bounded + 1,
            FormattableString.Invariant(
                $"a saturated RaycastAll reached {saturated} grid cells against the nearest cast's {bounded}, so it is still walking the grid past its limit."));
    }

    // A wide box lands on forty floor tiles and forty crates at once, more surfaces than the world's
    // scratch holds. The world is fresh, so its very first sweep is the one measured. An identical
    // world takes the first sweep beforehand, so compiling the code is not counted.
    [Fact]
    public void AFirstMoveMeetingMoreSurfacesThanTheScratchHolds_AllocatesNothingAndReportsThemAll()
    {
        Contact2D[] contacts = new Contact2D[128];
        Aabb2D box = Aabb2D.FromCorner(new Vector2(0f, -4f), new Vector2(80f * CollisionFixtures.TileSize, 8f));
        _ = LandingWorld(out _).MoveBox(box, new Vector2(0f, 24f), CollisionFilter.Everything, contacts);

        CollisionWorld2D world = LandingWorld(out GridCollider2D floor);
        long before = GC.GetAllocatedBytesForCurrentThread();
        MoveResult2D result = world.MoveBox(box, new Vector2(0f, 24f), CollisionFilter.Everything, contacts);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, bytes);
        Assert.Equal(80, result.ContactCount);
        Assert.All(contacts[..40], contact => Assert.Equal(floor.Handle, contact.Target.Collider));
        Assert.Equal(
            contacts[40..80].Select(contact => contact.Target.Collider.Index).Order(),
            contacts[40..80].Select(contact => contact.Target.Collider.Index));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AColliderWalkingASceneWithContactEvents_AllocatesNothingPerStep(bool restsOnCenter)
    {
        Scene scene = CollisionWorkload.Room();
        TileMap terrain = SceneFixtures.TerrainOf(scene);
        CollisionWorkload.Walker walker = new(CollisionWorkload.Mover.Min, restsOnCenter);
        scene.Add(walker);

        using SceneSimulation simulation = new(scene, run: StageWorkload.Defaults);

        // One input state for the run: building one a step is the harness allocating, not the step.
        InputState input = new(new ActionBindings());

        // A roof cell far from the walk is repainted every step, turning the other way each time.
        Report("walker in a scene", Measure(step =>
        {
            terrain.SetTile(200, 20, "slope-up", (step & 1) == 0 ? TileTransform.FlipX : TileTransform.Rotate90);
            simulation.Step(new StepContext(StageWorkload.StepSeconds, input, step));
            return walker.Contacts;
        }));

        // The walk went over the hill, so the measured steps include grounded moves on slope tiles.
        Assert.True(walker.Position.X > 528f);
    }

    // The lift carries a rider on every step, and on every stroke towards the crate it shoves the
    // crate that walks back against it.
    [Fact]
    public void APlatformCarryingARiderAndShovingABody_AllocatesNothingPerStep()
    {
        Scene scene = CollisionWorkload.Room();
        CollisionWorkload.Lift lift = new(new Vector2(96f, 632f));
        CollisionWorkload.Hauled rider = new(new Vector2(104f, 600f), walk: 0f);
        CollisionWorkload.Hauled crate = new(new Vector2(140f, 620f), walk: -1f);
        scene.Add(lift);
        scene.Add(rider);
        scene.Add(crate);

        using SceneSimulation simulation = new(scene, run: StageWorkload.Defaults);
        InputState input = new(new ActionBindings());
        float seat = 0f;

        Report("lift with a rider and a crate", Measure(step =>
        {
            simulation.Step(new StepContext(StageWorkload.StepSeconds, input, step));
            if (step == WarmupSteps)
            {
                seat = rider.Position.X - lift.Position.X;
            }

            return (int)crate.Position.X + crate.Crushes;
        }));

        // The rider kept its seat through every measured step, so the carry ran on each of them.
        Assert.Equal(seat, rider.Position.X - lift.Position.X, 0.01f);
        Assert.True(crate.Position.X > lift.Position.X);
    }

    // A sensor spans a row whose cells turn solid one a step, so its contact count rises by one on
    // every step. Buffers grown to the exact count would allocate on each of those steps. Doubling
    // allocates each buffer twice past its starting sixteen, and the bound is twice that.
    [Fact]
    public void ASensorWhoseContactsRiseOneAStep_GrowsItsBuffersGeometrically()
    {
        const int cells = 64;
        InputState input = new(new ActionBindings());
        using (SceneSimulation warm = RampScene(cells, out TileMap warmTerrain, out _))
        {
            Ramp(warm, warmTerrain, input, cells);
        }

        using SceneSimulation simulation = RampScene(cells, out TileMap terrain, out Sensor sensor);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Ramp(simulation, terrain, input, cells);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        long bound = 2 * 2 * cells * (Unsafe.SizeOf<Contact2D>() + (2 * Unsafe.SizeOf<ColliderContact2D>()));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"contacts rising to {cells}: {bytes} bytes, bound {bound}"));
        Assert.Equal(cells, sensor.Box.Touching.Length);
        Assert.True(bytes <= bound, $"a sensor whose contacts rose to {cells} one a step allocated {bytes} bytes, over the {bound} that doubling its buffers costs.");
    }

    // An empty row of cells under a sensor that spans it.
    private static SceneSimulation RampScene(int cells, out TileMap terrain, out Sensor sensor)
    {
        Scene scene = SceneFixtures.Terrain(new string('.', cells));
        terrain = SceneFixtures.TerrainOf(scene);
        sensor = new Sensor(new Vector2(1f, 4f), new Vector2((cells * SceneFixtures.TileSize) - 2f, 8f));
        scene.Add(sensor);

        return new SceneSimulation(scene, run: StageWorkload.Defaults);
    }

    private static void Ramp(SceneSimulation simulation, TileMap terrain, InputState input, int cells)
    {
        for (int step = 0; step < cells; step++)
        {
            terrain.SetTile(step, 0, "solid");
            simulation.Step(new StepContext(StageWorkload.StepSeconds, input, step));
        }
    }

    // Forty floor tiles with their tops at y = 16, and forty crates beside them level with the tiles.
    private static CollisionWorld2D LandingWorld(out GridCollider2D floor)
    {
        CollisionWorld2D world = new();
        floor = CollisionFixtures.Paint(world, new string('.', 40), new string('#', 40));
        CollisionLayer crate = world.Layer("crate");
        for (int index = 0; index < 40; index++)
        {
            world.Add(
                Shape2D.Box(Vector2.Zero, new Vector2(CollisionFixtures.TileSize)),
                new Vector2((40 + index) * CollisionFixtures.TileSize, CollisionFixtures.TileSize),
                crate);
        }

        return world;
    }

    private static (long Bytes, long Guard) Measure(Func<int, int> step)
    {
        long guard = 0;
        for (int index = 0; index < WarmupSteps; index++)
        {
            guard += step(index);
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < MeasuredSteps; index++)
        {
            guard += step(WarmupSteps + index);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - bytes, guard);
    }

    private void Report(string label, (long Bytes, long Guard) measured)
    {
        (long bytes, long guard) = measured;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label}: {bytes} bytes over {MeasuredSteps} steps (guard {guard})"));

        Assert.Equal(0, bytes);
    }

    private sealed class Sensor : Entity
    {
        internal BoxCollider2D Box { get; }

        internal Sensor(Vector2 position, Vector2 size)
            : base(position)
        {
            Box = new BoxCollider2D(size) { ReportsContacts = true };
            Box.SetFilter("solid");
            Add(Box);
        }
    }
}
