using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Runtime;

namespace Capsule.Tests.Scenes;

[Collection(LogSinkCollection.Name)]
public sealed class ScenePoolTests : IDisposable
{
    private static readonly TextureHandle ShotTexture = new("pooled/shot", ".png");
    private static readonly TextureHandle SparkTexture = new("pooled/spark", ".png");

    public void Dispose() => Log.UseSink(null);

    [Fact]
    public void Declarations_BuildOneScenePoolAtTheSumOfTheirCapacitiesAndPreloadIt()
    {
        Scene scene = new();
        scene.Add(new Gun(capacity: 2));
        scene.Add(new Gun(capacity: 5));

        scene.CollectPreloads();
        AssetCollection preloads = scene.CollectPreloads();

        EntityPool<Shot> pool = scene.Pool<Shot>();
        Assert.Same(pool, scene.Pool<Shot>());
        Assert.Equal(7, pool.Available);
        Assert.True(preloads.Contains(ShotTexture));
    }

    [Fact]
    public void AnEntityIdleInAForwardedPool_DeclaresTheScenePoolItTakesFrom()
    {
        Scene scene = new();
        scene.Add(new Launcher());

        Assert.True(scene.CollectPreloads().Contains(SparkTexture));
        Assert.Equal(6, scene.Pool<Spark>().Capacity);
    }

    // An echo declares the pool that holds it, whether taken into the scene or idle. Those declarations count
    // once beside the Echoer's, and the taken echo, reached twice, declares once.
    [Fact]
    public void PooledEntitiesDeclaringTheirOwnPool_CountOnceBesideOtherDeclarations()
    {
        Scene scene = new();
        scene.Add(new Echoer());
        scene.Add(scene.Pool<Echo>().Take());

        using SimulationHost host = new(scene);

        Assert.Equal(5, scene.Pool<Echo>().Capacity);
    }

    [Fact]
    public void APoolGrownAfterItWasCollected_HasItsNewEntitiesDeclare()
    {
        Scene scene = new ChainScene();

        scene.CollectPreloads();

        Assert.Equal(2, scene.Pool<ChainB>().Capacity);
        Assert.Equal(2, scene.Pool<ChainC>().Capacity);
    }

    [Fact]
    public void PoolsDeclaringEachOther_SettleWithTheBackDeclarationCountedOnce()
    {
        Scene scene = new CycleScene();

        scene.CollectPreloads();

        Assert.Equal(2, scene.Pool<CycleA>().Capacity);
        Assert.Equal(2, scene.Pool<CycleB>().Capacity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyAPoolNoDeclarationReached_LogsItsFirstTakeOnce(bool declared)
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);
        Scene scene = new();
        if (declared)
        {
            scene.Add(new Gun(capacity: 1));
        }

        using SimulationHost host = new(scene);
        scene.Add(scene.Pool<Shot>().Take());
        scene.Add(scene.Pool<Shot>().Take());

        Assert.Equal(
            declared ? 0 : 1,
            sink.Entries.Count(static entry => entry.Message.Contains("assets.Pool<Shot>", StringComparison.Ordinal)));
    }

    // Declares B before A, so A grows B after the collection first walked B.
    private sealed class ChainScene : Scene
    {
        protected internal override void CollectAssets(AssetCollection assets)
        {
            assets.Pool<ChainB>(capacity: 1);
            assets.Pool<ChainA>(capacity: 1);
        }
    }

    private sealed class ChainA() : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<ChainB>(capacity: 1);
    }

    private sealed class ChainB() : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<ChainC>(capacity: 1);
    }

    private sealed class ChainC() : Entity(Vector2.Zero);

    private sealed class CycleScene : Scene
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<CycleA>(capacity: 1);
    }

    private sealed class CycleA() : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<CycleB>(capacity: 1);
    }

    private sealed class CycleB() : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<CycleA>(capacity: 1);
    }

    private sealed class Gun(int capacity) : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<Shot>(capacity);
    }

    // Holds its own pool of shots that each burst into sparks from the scene's pool.
    private sealed class Launcher() : Entity(Vector2.Zero)
    {
        private readonly EntityPool<SparkingShot> _shots = new(() => new SparkingShot(), capacity: 2);

        protected internal override void CollectAssets(AssetCollection assets) => _shots.CollectAssets(assets);
    }

    private sealed class SparkingShot() : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<Spark>(capacity: 3);
    }

    private sealed class Echoer() : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<Echo>(capacity: 1);
    }

    // Each echo declares the pool of its own type that holds it.
    private sealed class Echo() : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<Echo>(capacity: 4);
    }

    private sealed class Shot : Entity
    {
        public Shot()
            : base(Vector2.Zero) => Add(new SpriteRenderer(new Sprite(ShotTexture, new TextureRegion(0, 0, 1, 1))));
    }

    private sealed class Spark : Entity
    {
        public Spark()
            : base(Vector2.Zero) => Add(new SpriteRenderer(new Sprite(SparkTexture, new TextureRegion(0, 0, 1, 1))));
    }
}
