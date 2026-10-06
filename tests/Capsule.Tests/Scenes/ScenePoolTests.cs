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
    public void Declarations_BuildOneScenePoolAtTheLargestCapacityAndPreloadIt()
    {
        Scene scene = new();
        scene.Add(new Gun(capacity: 2));
        scene.Add(new Gun(capacity: 5));

        AssetCollection preloads = scene.CollectPreloads();

        EntityPool<Shot> pool = scene.Pool<Shot>();
        Assert.Same(pool, scene.Pool<Shot>());
        Assert.Equal(5, pool.Available);
        Assert.True(preloads.Contains(ShotTexture));
    }

    [Fact]
    public void AnEntityIdleInAForwardedPool_DeclaresTheScenePoolItTakesFrom()
    {
        Scene scene = new();
        scene.Add(new Launcher());

        Assert.True(scene.CollectPreloads().Contains(SparkTexture));
        Assert.Equal(3, scene.Pool<Spark>().Capacity);
    }

    [Fact]
    public void APooledEntityDeclaringItsOwnPoolLarger_GrowsItWhileTheSceneCollects()
    {
        Scene scene = new();
        scene.Add(new Echoer());

        using SimulationHost host = new(scene);

        Assert.Equal(4, scene.Pool<Echo>().Available);
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

    private sealed class Gun(int capacity) : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<Shot>(capacity);
    }

    // Holds its own pool of shots that each burst into sparks from the scene's pool.
    private sealed class Launcher : Entity
    {
        private readonly EntityPool<SparkingShot> _shots = new(() => new SparkingShot(), capacity: 2);

        public Launcher()
            : base(Vector2.Zero)
        {
        }

        protected internal override void CollectAssets(AssetCollection assets) => _shots.CollectAssets(assets);
    }

    private sealed class SparkingShot : Entity
    {
        public SparkingShot()
            : base(Vector2.Zero)
        {
        }

        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<Spark>(capacity: 3);
    }

    private sealed class Echoer : Entity
    {
        public Echoer()
            : base(Vector2.Zero)
        {
        }

        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<Echo>(capacity: 1);
    }

    // Each idle echo asks for a larger pool of its own type than the scene first built.
    private sealed class Echo : Entity
    {
        public Echo()
            : base(Vector2.Zero)
        {
        }

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
