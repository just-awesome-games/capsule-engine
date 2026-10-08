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
    private const string Belt = "belt";

    private static readonly Vector2 Speed = new(60f, 0f);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABelt_CarriesTheBodyStandingOnIt_OneStepOfItsVelocityEachStep(bool collider)
    {
        Scene scene = collider ? new Scene() : SceneFixtures.Terrain(Grid("......", "......", ">>>>>>"));
        if (collider)
        {
            scene.Add(new Conveyor(new Vector2(0f, 32f), Speed));
        }

        Walker walker = new(new Vector2(8f, 16f), Belt) { Walks = true };
        Run(scene, walker, 10);

        Assert.Equal(18f, walker.Position.X, CollisionFixtures.Tolerance);
        Assert.True(walker.Mover.IsOnFloor);
    }

    // A collider with no surface velocity does not step. Setting one in the scene starts the carry, and
    // clearing it stops the carry.
    [Fact]
    public void ASurfaceVelocitySetAtRuntime_StartsAndStopsTheCarry()
    {
        Scene scene = new();
        Conveyor conveyor = new(new Vector2(0f, 32f), Vector2.Zero);
        scene.Add(conveyor);
        Walker walker = new(new Vector2(8f, 16f), Belt) { Walks = true };
        SceneSimulation simulation = Start(scene, walker);
        Advance(simulation, 0, 5);
        Assert.Equal(8f, walker.Position.X);

        conveyor.Surface.SurfaceVelocity = Speed;
        Advance(simulation, 5, 10);
        Assert.Equal(18f, walker.Position.X, CollisionFixtures.Tolerance);

        conveyor.Surface.SurfaceVelocity = Vector2.Zero;
        Advance(simulation, 15, 5);
        Assert.Equal(18f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    // Two belts meet at x = 96. The body reaches the seam on its 16th step, where the first belt hands it
    // to the second, which leaves it there until the next step.
    [Fact]
    public void ABodyCrossingTheSeamBetweenTwoBelts_IsCarriedOnceThatStep()
    {
        Scene scene = new();
        scene.Add(new Conveyor(new Vector2(0f, 32f), Speed));
        scene.Add(new Conveyor(new Vector2(96f, 32f), Speed));
        Walker walker = new(new Vector2(80f, 16f), Belt);
        SceneSimulation simulation = Start(scene, walker);
        Advance(simulation, 0, 15);
        Assert.Equal(95f, walker.Position.X, CollisionFixtures.Tolerance);

        Advance(simulation, 15, 1);

        Assert.Equal(96f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    // The body is carried at tick 0 in one scene, then reused in another whose host counts from tick 0 again.
    [Fact]
    public void ABodyReusedInANewScene_IsCarriedOnItsFirstStepThere()
    {
        Scene first = new();
        first.Add(new Conveyor(new Vector2(0f, 32f), Speed));
        Walker walker = new(new Vector2(8f, 16f), Belt);
        Advance(Start(first, walker), 0, 1);
        first.Remove(walker);

        Scene second = new();
        second.Add(new Conveyor(new Vector2(0f, 32f), Speed));
        SceneSimulation simulation = Start(second, walker);
        float before = walker.Position.X;
        Advance(simulation, 0, 1);

        Assert.Equal(before + 1f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    [Fact]
    public void AMirroredBeltCell_CarriesTheOtherWay()
    {
        Scene scene = SceneFixtures.Terrain(Grid("......", "......", ">>>>>>"));
        TileMap map = SceneFixtures.TerrainOf(scene);
        for (int x = 0; x < map.Width; x++)
        {
            map.SetTile(x, 2, "belt", TileTransform.FlipX);
        }

        Walker walker = new(new Vector2(40f, 16f), Belt);
        Run(scene, walker, 10);

        Assert.Equal(30f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    // The wall's face is at x = 48, so the 8 wide body stops with its corner at 40.
    [Fact]
    public void AWall_StopsTheCarry()
    {
        Walker walker = new(new Vector2(30f, 16f), Belt);
        Run(SceneFixtures.Terrain(Grid("......", "...#..", ">>>>>>")), walker, 20);

        Assert.Equal(40f, walker.Position.X, CollisionFixtures.Tolerance);
    }

    [Fact]
    public void ABodyNotMovedByTheBeltsLayer_IsNotCarried()
    {
        Walker walker = new(new Vector2(8f, 16f)) { Walks = true };
        Run(SceneFixtures.Terrain(Grid("......", "......", ">>>>>>")), walker, 10);

        Assert.Equal(8f, walker.Position.X);
    }

    // The belt ends at x = 48. The owner never moves the body, and once it stands wholly on the ground
    // past the belt, nothing carries it on.
    [Fact]
    public void ABodyLeavingABelt_KeepsNoneOfItsVelocity()
    {
        Walker walker = new(new Vector2(36f, 16f), Belt);
        SceneSimulation simulation = Start(SceneFixtures.Terrain(Grid("......", "......", ">>>###")), walker);
        Advance(simulation, 0, 20);
        float left = walker.Position.X;
        Assert.InRange(left, 48f, 49f + CollisionFixtures.Tolerance);

        Advance(simulation, 20, 20);

        Assert.Equal(left, walker.Position.X);
    }

    // A slide slope falls one unit for every unit across, from (0, 16) to (64, 80). A grounded body it
    // carries keeps its bottom-left corner on that line, where a straight carry would leave it in the air.
    [Fact]
    public void ACarryAlongASlope_WalksTheBodyDownIt()
    {
        Walker walker = new(new Vector2(4f, 0f), Belt);
        Run(SceneFixtures.Terrain(Grid("......", "\\.....", "#\\....", "##\\...", "###\\..", "######")), walker, 30);

        Assert.Equal(34f, walker.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(walker.Position.X + 16f - Walker.Edge, walker.Position.Y, CollisionFixtures.Tolerance);
    }

    // A body resting on its center sinks half its width into the slope. Carried off its foot onto the flat
    // ground at y = 80, it stands on that ground again with no move of its own.
    [Fact]
    public void ABodyRestingOnItsCenter_CarriedOffASlope_StandsOnTheFlatGround()
    {
        Walker walker = new(new Vector2(4f, 0f), Belt) { RestsOnCenter = true };
        Run(SceneFixtures.Terrain(Grid("......", "\\.....", "#\\....", "##\\...", "###\\..", "######")), walker, 80);

        Assert.InRange(walker.Position.X, 64f, 65f + CollisionFixtures.Tolerance);
        Assert.Equal(80f - Walker.Edge, walker.Position.Y, 0.01f);
    }

    private static void Run(Scene scene, Walker walker, int steps) => Advance(Start(scene, walker), 0, steps);

    // Adds the body and lands it once.
    private static SceneSimulation Start(Scene scene, Walker walker)
    {
        SceneSimulation simulation = new(scene);
        scene.Add(walker);
        walker.Mover.Move(new Vector2(0f, 64f));
        Assert.True(walker.Mover.IsOnFloor);

        return simulation;
    }

    private static void Advance(SceneSimulation simulation, int from, int steps)
    {
        for (int step = from; step < from + steps; step++)
        {
            simulation.Step(SceneFixtures.Step(step));
        }
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

    /// <summary>A belt that is an entity rather than tiles, 96 wide and 16 tall.</summary>
    private sealed class Conveyor : Entity
    {
        internal BoxCollider2D Surface { get; }

        internal Conveyor(Vector2 position, Vector2 velocity)
            : base(position)
        {
            Surface = new BoxCollider2D(new Vector2(96f, 16f)) { Layer = Belt, SurfaceVelocity = velocity };
            Add(Surface);
        }
    }

    /// <summary>An 8x8 grounded body on solid ground and belts, falling a unit a step while it walks.</summary>
    private sealed class Walker : Entity
    {
        internal const float Edge = 8f;

        internal Walker(Vector2 position, params string[] movedBy)
            : base(position)
        {
            BoxCollider2D collider = new(new Vector2(Edge, Edge));
            Add(collider);
            Mover = new KinematicBody2D(collider)
            {
                Mode = BodyMode.Grounded,
                BlockedBy = new(CollisionFixtures.Solid, Belt),
                MovedBy = new(movedBy),
            };
            Add(Mover);
        }

        internal KinematicBody2D Mover { get; }

        internal bool Walks { get; init; }

        internal bool RestsOnCenter
        {
            init => Mover.RestsOnCenter = value;
        }

        protected internal override void OnStep(in StepContext context)
        {
            if (Walks)
            {
                Mover.Move(new Vector2(0f, 1f));
            }
        }
    }
}
