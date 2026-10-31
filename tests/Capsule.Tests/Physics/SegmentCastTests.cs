using System.Numerics;
using Capsule.Physics;

namespace Capsule.Tests.Physics;

// The particle step's segment cast skips the walk for a move inside one empty cell. It must answer as the full ray does.
public sealed class SegmentCastTests
{
    [Fact]
    public void ASegment_HitsExactlyWhatTheRayAlongItHits_OverEveryKindOfCellAndACollider()
    {
        CollisionWorld2D world = new();
        CollisionFixtures.Paint(
            world,
            "......",
            ".-=/\\.",
            "..#...",
            "######");
        CollisionLayer wall = world.Layer("wall");
        world.Add(Shape2D.Box(new Vector2(70f, 4f), new Vector2(6f, 20f)), Vector2.Zero, wall);
        CollisionFilter gridsOnly = CollisionFilter.Everything.Without(wall);

        Random random = new(11);
        int hits = 0;
        for (int index = 0; index < 20_000; index++)
        {
            Vector2 from = new(random.NextSingle() * 104f - 4f, random.NextSingle() * 72f - 4f);
            Vector2 to = from + new Vector2(random.NextSingle() * 24f - 12f, random.NextSingle() * 24f - 12f);
            CollisionFilter filter = (index & 1) == 0 ? CollisionFilter.Everything : gridsOnly;

            bool segment = world.SegmentCast(from, to, filter, out RayHit2D segmentHit);
            float distance = Vector2.Distance(from, to);
            RayHit2D rayHit = default;
            bool ray = distance > 0f && world.Raycast(from, to - from, distance, filter, out rayHit);

            Assert.Equal(ray, segment);
            Assert.Equal(rayHit, segmentHit);
            hits += segment ? 1 : 0;
        }

        Assert.InRange(hits, 2_000, 18_000);
    }
}
