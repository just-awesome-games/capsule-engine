using System.Numerics;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Tests.Allocation;

// A pool two scenes share holds entities the outgoing scene still runs while the incoming one preloads. The incoming
// scene reserves their room all the same.
[Collection(StageAllocationCollection.Name)]
public sealed class SharedPoolAllocationTests
{
    // More than a new collision world holds before it grows.
    private const int Pooled = 64;

    [Fact]
    public void ASharedPoolsEntities_JoinTheNextSceneWithoutAllocating()
    {
        // A first transition runs every code path once. Only the second is measured.
        Transition();
        long bytes = Transition();

        Assert.True(bytes == 0, $"Joining the shared pool's entities allocated {bytes} bytes.");
    }

    private static long Transition()
    {
        EntityPool<Crate> crates = new(static () => new Crate(), Pooled);
        Scene outgoing = new();
        outgoing.Add(new Holder(crates));
        SceneSimulation running = new(outgoing);
        for (int index = 0; index < Pooled; index++)
        {
            outgoing.Add(crates.Take());
        }

        // The host collects the incoming scene's preloads before the outgoing one stops.
        Scene incoming = new();
        incoming.Add(new Holder(crates));
        incoming.CollectAssetPreloads();
        running.Dispose();

        using SceneSimulation next = new(incoming);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < Pooled; index++)
        {
            incoming.Add(crates.Take());
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class Holder(EntityPool<Crate> crates) : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => crates.CollectAssets(assets);
    }

    private sealed class Crate : Entity
    {
        internal Crate()
            : base(Vector2.Zero) => Add(new BoxCollider2D(new Vector2(8f, 8f)));
    }
}
