using System.Numerics;
using Capsule.Physics;
using Capsule.Tests.Physics;

namespace Capsule.Tests.Allocation;

// What a collider costs to make, which a crowd of thousands pays once per member.
[Collection(StageAllocationCollection.Name)]
public sealed class ColliderFootprintTests(ITestOutputHelper output)
{
    private const int Colliders = 64;

    // A reporter's three contact buffers of starting size come to about 650 bytes, and the sixteen-slot
    // buffers every collider once carried came to almost 4 KB. A collider that never reports holds none
    // of them.
    private const long BytesPerCollider = 512;

    [Fact]
    public void AColliderThatNeverReportsContacts_CarriesNoContactBuffers()
    {
        // The first collider takes the type's one-time costs.
        _ = new BoxCollider2D(new Vector2(16f, 16f));
        BoxCollider2D[] made = new BoxCollider2D[Colliders];

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < Colliders; index++)
        {
            made[index] = new BoxCollider2D(new Vector2(16f, 16f));
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine(FormattableString.Invariant($"{bytes / Colliders} bytes per collider, budget {BytesPerCollider}"));
        Assert.True(
            bytes <= BytesPerCollider * Colliders,
            FormattableString.Invariant($"a collider took {bytes / Colliders} bytes, over its budget of {BytesPerCollider}."));
    }

    // A reservation holds a slot, two tree nodes and the build's lists for each collider, about 350
    // bytes. A candidate list for every slot added 120 more, and only a collider reporting contacts
    // ever fills one.
    [Fact]
    public void ReservingForCollidersThatNeverReportContacts_HoldsNoCandidateLists()
    {
        const int Reserved = 1024;
        const long BytesPerReserved = 384;
        CollisionWorld2D world = new();

        long before = GC.GetAllocatedBytesForCurrentThread();
        world.Reserve(Reserved, 0, 0);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine(FormattableString.Invariant($"{bytes / Reserved} bytes per reserved collider, budget {BytesPerReserved}"));
        Assert.True(
            bytes <= BytesPerReserved * Reserved,
            FormattableString.Invariant($"a reserved collider took {bytes / Reserved} bytes, over its budget of {BytesPerReserved}."));
    }

    // A tile palette is mostly art that collides as nothing. Such an entry costs a grid its flags and
    // spans, about 60 bytes, and none of the edge, polygon and wall room a shaped entry takes.
    [Fact]
    public void AGridOverAPaletteThatMostlyCollidesAsNothing_HoldsNoEdgesForThoseEntries()
    {
        const int Entries = 1024;
        const long BytesPerEntry = 96;
        CollisionWorld2D world = new();
        CellProfile2D[] profiles = new CellProfile2D[Entries];
        profiles[1] = new CellProfile2D(world.Layer(CollisionFixtures.Solid), CollisionFixtures.SlopeUp);
        int[] lookup = [.. Enumerable.Range(0, Entries)];
        CellBlocks cells = CellBlocks.FromDense(16, 16, new int[16 * 16], default);

        long before = GC.GetAllocatedBytesForCurrentThread();
        world.AddGrid(CollisionFixtures.TileSize, cells, lookup, 1, profiles);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine(FormattableString.Invariant($"{bytes / Entries} bytes per palette entry, budget {BytesPerEntry}"));
        Assert.True(
            bytes <= BytesPerEntry * Entries,
            FormattableString.Invariant($"a palette entry took {bytes / Entries} bytes, over its budget of {BytesPerEntry}."));
    }
}
