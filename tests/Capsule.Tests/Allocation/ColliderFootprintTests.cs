using System.Numerics;
using Capsule.Physics;
using Xunit.Abstractions;

namespace Capsule.Tests.Allocation;

// What a collider costs to make, which a crowd of thousands pays once per member.
[Collection(StageAllocationCollection.Name)]
public sealed class ColliderFootprintTests(ITestOutputHelper output)
{
    private const int Colliders = 64;

    // Four contact buffers of starting size come to about 1 KB, and the sixteen-slot buffers every
    // collider once carried came to almost 4 KB. A collider that never reports holds none of them.
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
}
