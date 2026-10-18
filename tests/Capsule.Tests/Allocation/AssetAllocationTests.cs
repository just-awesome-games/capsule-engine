using Capsule.Assets;
using Capsule.Runtime.Assets;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class AssetAllocationTests
{
    [Fact]
    public void SceneAssetCacheHits_AllocateNothing()
    {
        TextureHandle handle = new("hero", ".png");
        using SceneAssetStore<TextureHandle, DisposalProbe> store = SyncStore.Over<TextureHandle, DisposalProbe>(
            static _ => new DisposalProbe());
        _ = store.Get(handle);

        Assert.Equal(0, StepMeasurement.Bytes(_ => store.Get(handle), warmup: 100, measured: 1000));
    }
}
