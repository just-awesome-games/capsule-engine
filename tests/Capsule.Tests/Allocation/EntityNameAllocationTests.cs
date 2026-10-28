using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

// A join numbers the entity's name, and reading an unnamed entity's type name allocates nothing even once full
// collections have dropped the runtime's own cache of it.
[Collection(StageAllocationCollection.Name)]
public sealed class EntityNameAllocationTests
{
    private const int WarmupRounds = 2;
    private const int MeasuredRounds = 8;

    // Each round takes the pooled entity, joins it, and returns it to the pool.
    [Fact]
    public void APooledJoin_AllocatesNothing_AcrossFullCollections()
    {
        Scene scene = new();
        EntityPool<Pooled> pool = scene.Pool<Pooled>();
        using SimulationHost host = new(scene);

        long bytes = 0;
        for (int round = 0; round < WarmupRounds + MeasuredRounds; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            Pooled entity = pool.Take();
            scene.Add(entity);
            host.Step();
            scene.Remove(entity);
            host.Step();
            if (round >= WarmupRounds)
            {
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            }
        }

        Assert.True(bytes == 0, $"{MeasuredRounds} pooled joins across full collections allocated {bytes} bytes.");
    }

    private sealed class Pooled : Entity;
}
