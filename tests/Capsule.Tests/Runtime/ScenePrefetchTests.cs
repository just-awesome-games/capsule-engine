using Capsule.Assets;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Allocation;
using static Capsule.Tests.Runtime.SceneHostFixtures;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Runtime;

// A prefetch decodes on the thread pool, which the heap-measuring specs must not see.
[Collection(StageAllocationCollection.Name)]
public sealed class ScenePrefetchTests
{
    private static readonly TextureHandle Hud = new("hud", ".png");

    private static readonly TextureHandle Shared = new("shared", ".png");

    private static readonly TextureHandle Bat = new("enemies/bat", ".png");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APrefetch_BuildsItsSceneAfterTheStepThatAskedAndNeverStartsIt(bool device)
    {
        List<string> log = [];
        List<string> warmed = [];
        using SceneHost host = new(ToScene<Lobby>(), Resolver(log), new Run());
        if (device)
        {
            host.PrefetchAssets = assets => warmed.Add(string.Join(",", assets.Textures.Select(static handle => handle.Name)));
        }

        host.Step(Step(0));

        Assert.Equal(["resolve Lobby", "lobby step", "resolve Arena", "arena built"], log);
        Assert.IsType<Lobby>(host.Scene);
        Assert.Equal(device ? ["shared,enemies/bat"] : [], warmed);
    }

    [Fact]
    public void ARepeatedPrefetch_DoesNothing()
    {
        List<string> log = [];
        int warmed = 0;
        using SceneHost host = new(ToScene<Lobby>(), Resolver(log), new Run());
        host.PrefetchAssets = _ => warmed++;

        host.Step(Step(0));
        host.Step(Step(1));
        host.Step(Step(2));

        Assert.Single(log, static entry => entry == "resolve Arena");
        Assert.Equal(1, warmed);
    }

    [Fact]
    public void ADifferentPrefetchOrARequest_ReleasesTheUnsharedWarmSet()
    {
        Dictionary<string, List<FakeAsset>> created = [];
        using var store = SyncStore.Over<TextureHandle, FakeAsset>(handle =>
        {
            FakeAsset asset = new();
            created.TryAdd(handle.Name, []);
            created[handle.Name].Add(asset);
            return asset;
        });

        store.Prefetch([Hud, Shared]);
        PumpUntil(store, () => created.Count == 2);
        store.Prefetch([Shared, Bat]);
        PumpUntil(store, () => created.Count == 3);

        Assert.True(created["hud"][0].Disposed);
        Assert.False(created["shared"][0].Disposed);

        store.ChangeScene([Bat]);

        Assert.True(created["shared"][0].Disposed);
        Assert.False(created["enemies/bat"][0].Disposed);
        Assert.Same(created["enemies/bat"][0], store.Get(Bat));
        Assert.All(created.Values, static assets => Assert.Single(assets));
    }

    private static SceneResolver Resolver(List<string> log) => (in SceneTransition target) =>
    {
        log.Add("resolve " + target.SceneType!.Name);
        return target.SceneType == typeof(Lobby) ? new Lobby(log) : new Arena(log);
    };

    private static void PumpUntil(Capsule.Runtime.Assets.SceneAssetStore<TextureHandle, FakeAsset> store, Func<bool> done)
    {
        for (int frame = 0; frame < 1000 && !done(); frame++)
        {
            store.Pump();
            Thread.Sleep(1);
        }

        Assert.True(done(), "the prefetch decodes never landed");
    }

    private sealed class Lobby(List<string> log) : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            log.Add("lobby step");
            Run.PrefetchScene<Arena>();
        }
    }

    private sealed class Arena : Scene
    {
        public Arena(List<string> log) => log.Add("arena built");

        protected internal override void CollectAssets(AssetCollection assets) => assets.Add([Shared, Bat]);

        protected override void OnStart() => throw new InvalidOperationException("A prefetched scene must not start.");
    }

    private sealed class FakeAsset : IDisposable
    {
        internal bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
