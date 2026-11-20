using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

// A move pressed into the crease where two surfaces meet, which stops there rather than sliding along one and back.
public sealed class CreaseTests
{
    // A floor meeting a 45 degree slope is a 135 degree crease. A box pressed into it from between the two normals
    // comes to rest at the foot and stays there, rather than climbing the slope and falling back each step.
    [Fact]
    public void MoveBox_PressedIntoAnObtuseCrease_ComesToRest()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(world, "......", "......", "...../", "######");
        Vector2 velocity = new Vector2(MathF.Cos(MathF.PI / 3f), MathF.Sin(MathF.PI / 3f)) * 2f;
        Aabb2D box = CollisionFixtures.Box(40f, 30f, 8f, 8f);
        for (int step = 0; step < 300; step++)
        {
            box = box.Translated(world.MoveBox(box, velocity, CollisionFilter.Everything, default).Translation);
        }

        Assert.Equal(72f, box.Min.X, 0.02f);
        Assert.Equal(40f - CollisionTolerance.LinearSlop, box.Min.Y, 0.02f);
    }

    // A grounded body falling into a valley whose two faces are too steep to stand on settles where they meet. Its
    // fall slides down one face into the other and stops there, and the next step's fall finds it in the same place.
    [Fact]
    public void AGroundedBodyFallingIntoAValleyOfWalls_SettlesWithoutShaking()
    {
        Scene scene = new Scene(SceneFixtures.Content(SceneFixtures.Room(), SceneFixtures.Registry())).Started();
        scene.Add(new Valley());
        SceneFixtures.Body body = new(new Vector2(100f, 40f), blocksOn: CollisionFixtures.Solid);
        body.Mover.Mode = BodyMode.Grounded;
        body.Mover.MaxFloorAngle = 30f;
        scene.Add(body);

        List<Vector2> rest = [];
        for (int step = 0; step < 200; step++)
        {
            body.Mover.Move(new Vector2(0f, 2f));
            if (step >= 180)
            {
                rest.Add(body.Position);
            }
        }

        Assert.All(rest, at => Assert.Equal(rest[0], at));
        Assert.False(body.Mover.IsOnFloor);
    }

    // Two faces about 40 degrees from level meeting at (120, 100), solid below both.
    private sealed class Valley : Entity
    {
        internal Valley()
            : base(Vector2.Zero)
        {
            Add(new PolygonCollider2D([new(0f, 0f), new(120f, 100f), new(0f, 100f)]) { Layer = "solid" });
            Add(new PolygonCollider2D([new(120f, 100f), new(240f, 0f), new(240f, 100f)]) { Layer = "solid" });
        }
    }
}
