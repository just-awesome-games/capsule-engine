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
    private static readonly TextureHandle First = new("first", ".png");

    private static readonly TextureHandle Shared = new("shared", ".png");

    private static readonly TextureHandle Nested = new("nested/second", ".png");

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

        // The built scene composed an entity whose hooks never ran, as it never joined.
        Assert.Equal(["resolve Lobby", "lobby step", "resolve Arena", "arena built"], log);
        Assert.IsType<Lobby>(host.Scene);
        Assert.Equal(device ? ["shared,nested/second"] : [], warmed);
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
    public void APrefetch_CollectsWithItsPayload_AndARequestWithAnotherPayloadCollectsAgain()
    {
        using SceneHost host = new(
            ToScene<Scene>(),
            static (in SceneTransition target) => target.SceneType == typeof(DeclaresFromPayload) ? new DeclaresFromPayload() : new Scene(),
            new Run());
        List<string> warmed = [];
        List<string> prepared = [];
        host.PrefetchAssets = assets => warmed.Add(string.Join(",", assets.Textures.Select(static handle => handle.Name)));
        host.PrepareAssets = assets => prepared.Add(string.Join(",", assets.Textures.Select(static handle => handle.Name)));

        host.Run.PrefetchScene<DeclaresFromPayload>(new Tag("north"));
        host.Step(Step(0));
        host.Run.PrefetchScene<DeclaresFromPayload>(new Tag("north"));
        host.Step(Step(1));
        host.Run.PrefetchScene<DeclaresFromPayload>(new Tag("south"));
        host.Step(Step(2));
        host.Run.RequestScene<DeclaresFromPayload>(new Tag("east"));
        host.Step(Step(3));

        Assert.Equal(["north", "south"], warmed);
        Assert.Equal(["east"], prepared);
    }

    [Fact]
    public void ADifferentPrefetchOrARequest_ReleasesTheUnsharedWarmSet()
    {
        Dictionary<string, List<DisposalProbe>> created = [];
        using var store = SyncStore.Over<TextureHandle, DisposalProbe>(handle =>
        {
            DisposalProbe asset = new();
            created.TryAdd(handle.Name, []);
            created[handle.Name].Add(asset);
            return asset;
        });

        store.Prefetch([First, Shared]);
        PumpUntil(store, () => created.Count == 2);
        store.Prefetch([Shared, Nested]);
        PumpUntil(store, () => created.Count == 3);

        Assert.True(created["first"][0].Disposed);
        Assert.False(created["shared"][0].Disposed);

        store.ChangeScene([Nested]);

        Assert.True(created["shared"][0].Disposed);
        Assert.False(created["nested/second"][0].Disposed);
        Assert.Same(created["nested/second"][0], store.Get(Nested));
        Assert.All(created.Values, static assets => Assert.Single(assets));
    }

    private static SceneResolver Resolver(List<string> log) => (in SceneTransition target) =>
    {
        log.Add("resolve " + target.SceneType!.Name);
        return target.SceneType == typeof(Lobby) ? new Lobby(log) : new Arena(log);
    };

    private static void PumpUntil(Capsule.Runtime.Assets.SceneAssetStore<TextureHandle, DisposalProbe> store, Func<bool> done)
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
        public Arena(List<string> log)
        {
            Add(new Recorder("arena entity", log));
            log.Add("arena built");
        }

        protected internal override void CollectAssets(AssetCollection assets) => assets.Add([Shared, Nested]);

        protected override void OnStart() => throw new InvalidOperationException("A prefetched scene must not start.");
    }
}
