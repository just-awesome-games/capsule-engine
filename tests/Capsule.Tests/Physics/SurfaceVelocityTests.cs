using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Physics;

// A surface velocity carries the bodies standing on it without moving the surface. Every belt here
// runs at 60 units a second, one unit a step.
public sealed class SurfaceVelocityTests
{
    private const string Belt = CollisionFixtures.Platform;

    private static readonly Vector2 Speed = new(60f, 0f);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABelt_CarriesTheBodyStandingOnIt_OneStepOfItsVelocityEachStep(bool collider)
    {
        Scene scene = Belted(collider);
        Rider walker = new(new Vector2(8f, 16f), Belt) { Falls = true };
        Run(scene, walker, 10);

        Assert.Equal(18f, walker.Position.X, CollisionFixtures.Tolerance);
        Assert.True(walker.Mover.IsOnFloor);
    }

    // Setting a surface velocity in the scene starts the carry, and clearing it stops the carry.
    [Fact]
    public void ASurfaceVelocitySetAtRuntime_StartsAndStopsTheCarry()
    {
        Scene scene = new();
        Slab conveyor = scene.Add(Conveyor(new Vector2(0f, 32f), Vector2.Zero));
        Rider walker = new(new Vector2(8f, 16f), Belt) { Falls = true };
        SceneSimulation simulation = Start(scene, walker);
        CollisionFixtures.Advance(simulation, 0, 5);
        Assert.Equal(8f, walker.Position.X);

        conveyor.Collider.SurfaceVelocity = Speed;
        CollisionFixtures.Advance(simulation, 5, 10);
        Assert.Equal(18f, walker.Position.X, CollisionFixtures.Tolerance);

        conveyor.Collider.SurfaceVelocity = Vector2.Zero;
        CollisionFixtures.Advance(simulation, 15, 5);
        Assert.Equal(18f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    // The body carries itself. A pause that holds the belt still carries a body stepping through it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHeldBelt_CarriesABodyThatSteps(bool collider)
    {
        Scene scene = Belted(collider);
        Rider walker = new(new Vector2(8f, 16f), Belt) { StepMode = StepMode.Always };
        SceneSimulation simulation = Start(scene, walker);
        scene.Paused = true;

        CollisionFixtures.Advance(simulation, 0, 10);

        Assert.Equal(18f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    [Fact]
    public void AHeldBody_IsNotCarriedByABeltThatSteps()
    {
        Scene scene = new();
        scene.Add(Conveyor(new Vector2(0f, 32f))).StepMode = StepMode.Always;
        Rider walker = new(new Vector2(8f, 16f), Belt);
        SceneSimulation simulation = Start(scene, walker);
        scene.Paused = true;

        CollisionFixtures.Advance(simulation, 0, 10);

        Assert.Equal(8f, walker.Position.X);
    }

    // The body never moves itself, and only a move finds a floor. A map that leaves the world and returns
    // before the body steps has ended the ride all the same.
    [Fact]
    public void AMapThatLeftTheWorldAndReturned_NoLongerCarriesTheBodyThatRodeIt()
    {
        Scene scene = Belted(collider: false);
        Rider walker = new(new Vector2(8f, 16f), Belt);
        SceneSimulation simulation = Start(scene, walker);
        CollisionFixtures.Advance(simulation, 0, 1);
        float carried = walker.Position.X;
        TileMapCollider2D ground = SceneFixtures.TerrainOf(scene).Get<TileMapCollider2D>();

        ground.Enabled = false;
        ground.Enabled = true;
        CollisionFixtures.Advance(simulation, 1, 5);

        Assert.Equal(carried, walker.Position.X);
    }

    // Two belts meet at x = 96. The body reaches the seam on its 16th step, where the first belt hands it
    // to the second, which leaves it there until the next step.
    [Fact]
    public void ABodyCrossingTheSeamBetweenTwoBelts_IsCarriedOnceThatStep()
    {
        Scene scene = new();
        scene.Add(Conveyor(new Vector2(0f, 32f)));
        scene.Add(Conveyor(new Vector2(96f, 32f)));
        Rider walker = new(new Vector2(80f, 16f), Belt);
        SceneSimulation simulation = Start(scene, walker);
        CollisionFixtures.Advance(simulation, 0, 15);
        Assert.Equal(95f, walker.Position.X, CollisionFixtures.Tolerance);

        CollisionFixtures.Advance(simulation, 15, 1);

        Assert.Equal(96f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    // The body is carried at tick 0 in one scene, then reused in another whose host counts from tick 0 again.
    [Fact]
    public void ABodyReusedInANewScene_IsCarriedOnItsFirstStepThere()
    {
        Scene first = new();
        first.Add(Conveyor(new Vector2(0f, 32f)));
        Rider walker = new(new Vector2(8f, 16f), Belt);
        CollisionFixtures.Advance(Start(first, walker), 0, 1);
        first.Remove(walker);

        Scene second = new();
        second.Add(Conveyor(new Vector2(0f, 32f)));
        SceneSimulation simulation = Start(second, walker);
        float before = walker.Position.X;
        CollisionFixtures.Advance(simulation, 0, 1);

        Assert.Equal(before + 1f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    // Contacts list in the order their colliders registered. Sixteen floors registered before the belt and
    // moved under a floating body that landed on the belt alone fill the room its moves have needed. A
    // floating carry sweeps past them without growing it. The belt after them still carries the body.
    [Fact]
    public void ABeltAmongMoreFloorsThanTheBodysMovesHaveMet_KeepsCarrying()
    {
        Scene scene = new();
        Slab[] slabs = new Slab[16];
        for (int index = 0; index < slabs.Length; index++)
        {
            slabs[index] = scene.Add(new Slab(new Vector2(500f, 32f), CollisionFixtures.Solid, new Vector2(96f, 16f)));
        }

        scene.Add(Conveyor(new Vector2(0f, 32f)));
        Rider walker = new(new Vector2(8f, 16f), Belt);
        SceneSimulation simulation = Start(scene, walker);
        walker.Mover.Mode = BodyMode.Floating;
        foreach (Slab slab in slabs)
        {
            slab.Position = new Vector2(0f, 32f);
        }

        CollisionFixtures.Advance(simulation, 0, 10);

        Assert.Equal(18f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    [Fact]
    public void AMirroredBeltCell_CarriesTheOtherWay()
    {
        Scene scene = Belted(collider: false);
        TileMap map = SceneFixtures.TerrainOf(scene);
        for (int x = 0; x < map.Width; x++)
        {
            map.SetTile(x, 2, "belt", TileTransform.FlipX);
        }

        Rider walker = new(new Vector2(40f, 16f), Belt);
        Run(scene, walker, 10);

        Assert.Equal(30f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    // The wall's face is at x = 48. The 8 wide body stops with its corner at 40.
    [Fact]
    public void AWall_StopsTheCarry()
    {
        Rider walker = new(new Vector2(30f, 16f), Belt);
        Run(SceneFixtures.Terrain(Grid("......", "...#..", ">>>>>>")), walker, 20);

        Assert.Equal(40f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    [Fact]
    public void ABodyNotMovedByTheBeltsLayer_IsNotCarried()
    {
        Rider walker = new(new Vector2(8f, 16f)) { Falls = true };
        Run(Belted(collider: false), walker, 10);

        Assert.Equal(8f, walker.Position.X);
    }

    // The belt ends at x = 48. The owner never moves the body, and once it stands wholly on the ground
    // past the belt, nothing carries it on.
    [Fact]
    public void ABodyLeavingABelt_KeepsNoneOfItsVelocity()
    {
        Rider walker = new(new Vector2(36f, 16f), Belt);
        SceneSimulation simulation = Start(SceneFixtures.Terrain(Grid("......", "......", ">>>###")), walker);
        CollisionFixtures.Advance(simulation, 0, 20);
        float left = walker.Position.X;
        Assert.InRange(left, 48f, 49f + CollisionFixtures.Tolerance);

        CollisionFixtures.Advance(simulation, 20, 20);

        Assert.Equal(left, walker.Position.X);
    }

    // A slide slope falls one unit for every unit across, from (0, 16) to (64, 80). A grounded body it
    // carries keeps its bottom-left corner on that line, where a straight carry would leave it in the air.
    [Fact]
    public void ACarryAlongASlope_WalksTheBodyDownIt()
    {
        Rider walker = new(new Vector2(4f, 0f), Belt);
        Run(SceneFixtures.Terrain(Grid("......", "\\.....", "#\\....", "##\\...", "###\\..", "######")), walker, 30);

        Assert.Equal(34f, walker.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(walker.Position.X + 16f - Rider.Edge, walker.Position.Y, CollisionFixtures.Tolerance);
    }

    // A body resting on its center sinks half its width into the slope. Carried off its foot onto the flat
    // ground at y = 80, it stands on that ground again with no move of its own.
    [Fact]
    public void ABodyRestingOnItsCenter_CarriedOffASlope_StandsOnTheFlatGround()
    {
        Rider walker = new(new Vector2(4f, 0f), Belt) { Mover = { RestsOnCenter = true } };
        Run(SceneFixtures.Terrain(Grid("......", "\\.....", "#\\....", "##\\...", "###\\..", "######")), walker, 80);

        Assert.InRange(walker.Position.X, 64f, 65f + CollisionFixtures.Tolerance);
        Assert.Equal(80f - Rider.Edge, walker.Position.Y, 0.01f);
    }

    // A belt along y = 32 from x = 0 to 96, as a collider or as a row of belt tiles.
    private static Scene Belted(bool collider)
    {
        if (!collider)
        {
            return SceneFixtures.Terrain(Grid("......", "......", ">>>>>>"));
        }

        Scene scene = new();
        scene.Add(Conveyor(new Vector2(0f, 32f)));

        return scene;
    }

    // A belt that is an entity rather than tiles, 96 wide and 16 tall, running at Speed unless given another.
    private static Slab Conveyor(Vector2 position, Vector2? velocity = null) =>
        new(position, Belt, new Vector2(96f, 16f)) { Collider = { SurfaceVelocity = velocity ?? Speed } };

    private static void Run(Scene scene, Rider walker, int steps) => CollisionFixtures.Advance(Start(scene, walker), 0, steps);

    // Grounds the body, adds it and lands it once.
    private static SceneSimulation Start(Scene scene, Rider walker)
    {
        SceneSimulation simulation = new(scene);
        walker.Mover.Mode = BodyMode.Grounded;
        scene.Add(walker);
        CollisionFixtures.Land(walker.Mover, 64f);

        return simulation;
    }

    // '#' is solid ground, '>' a belt running right and '\' a slide slope falling to the right that
    // carries the same way.
    private static TileGrid Grid(params string[] rows)
    {
        int width = rows[0].Length;
        int[] cells = new int[width * rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells[(y * width) + x] = rows[y][x] switch
                {
                    '#' => 1,
                    '>' => 2,
                    '\\' => 3,
                    _ => 0,
                };
            }
        }

        return new TileGrid(
            SceneFixtures.TileSize,
            width,
            rows.Length,
            [
                TileGrid.EmptyTile,
                new TileType { Name = "solid", Cell = 0, Layer = CollisionFixtures.Solid },
                new TileType { Name = "belt", Cell = 0, Layer = Belt, SurfaceVelocity = Speed },
                new TileType { Name = "slide", Cell = 0, Layer = Belt, Shape = CollisionFixtures.SlopeDownPoints, SurfaceVelocity = Speed },
            ],
            cells,
            SceneFixtures.Atlas,
            1);
    }
}
