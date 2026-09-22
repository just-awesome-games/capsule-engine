using Capsule.Assets;
using Capsule.Runtime.Assets;
using Capsule.Tests.Runtime;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class AssetAllocationTests
{
    [Fact]
    public void SceneAssetCacheHits_AllocateNothing()
    {
        TextureHandle handle = new("hero", ".png");
        using SceneAssetStore<TextureHandle, FakeAsset> store = SyncStore.Over<TextureHandle, FakeAsset>(
            static _ => new FakeAsset());

        FakeAsset asset = store.Get(handle);
        for (int i = 0; i < 100; i++)
        {
            asset = store.Get(handle);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            asset = store.Get(handle);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        GC.KeepAlive(asset);
        Assert.Equal(0, allocated);
    }

    private sealed class FakeAsset : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
