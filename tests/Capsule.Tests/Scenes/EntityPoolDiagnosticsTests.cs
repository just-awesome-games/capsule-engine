using System.Numerics;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Runtime;

namespace Capsule.Tests.Scenes;

[Collection(LogSinkCollection.Name)]
public sealed class EntityPoolDiagnosticsTests : IDisposable
{
    private static readonly TextureHandle Shot = new("pooled/shot", ".png");
    private static readonly AudioClip Streamed = new("pooled/hum", ".ogg", 1d);

    public void Dispose() => Log.UseSink(null);

    [Fact]
    public void AnUnforwardedPoolWithAssets_LogsItsTypeOnceAtTheFirstTake()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);
        EntityPool<Pooled> pool = new(() => new Pooled(Shot), capacity: 2);

        pool.Take();
        pool.Take();

        LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(LogLevel.Info, entry.Level);
        Assert.Contains("EntityPool<Pooled>", entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("forwarded")]
    [InlineData("white texel only")]
    [InlineData("streamed clip only")]
    public void AForwardedPoolOrOneHoldingNothingToPreload_StaysQuiet(string setup)
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);
        EntityPool<Pooled> pool = new(
            () => setup switch
            {
                "white texel only" => new Pooled(TextureHandle.White),
                "streamed clip only" => new Pooled(TextureHandle.White, Streamed),
                _ => new Pooled(Shot),
            },
            capacity: 1);
        if (setup == "forwarded")
        {
            pool.CollectAssets(new AssetCollection());
        }

        pool.Take();

        Assert.Empty(sink.Entries);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHeadlessHost_CollectsBeforeStartEvenAfterARead_SoOnlyAnUnforwardedPoolLogs(bool forwarded)
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);
        Scene scene = new();
        scene.Add(new PoolOwner(forwarded));
        scene.CollectPreloads();

        using SimulationHost host = new(scene);

        Assert.Equal(
            forwarded ? 0 : 1,
            sink.Entries.Count(static entry => entry.Message.Contains("EntityPool<Pooled>", StringComparison.Ordinal)));
    }

    [Fact]
    public void ReadingAScenesPreloads_ForwardsNoPool()
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);
        EntityPool<Pooled> pool = new(() => new Pooled(Shot), capacity: 1);
        Scene scene = new();
        scene.Add(new Forwarder(pool));

        Assert.True(scene.CollectPreloads().Contains(Shot));
        pool.Take();

        Assert.Single(sink.Entries);
    }

    private sealed class Forwarder(EntityPool<Pooled> pool) : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => pool.CollectAssets(assets);
    }

    // Takes in its start hook, the earliest a game can.
    private sealed class PoolOwner(bool forwards) : Entity(Vector2.Zero)
    {
        private readonly EntityPool<Pooled> _pool = new(() => new Pooled(Shot), capacity: 1);

        protected internal override void CollectAssets(AssetCollection assets)
        {
            if (forwards)
            {
                _pool.CollectAssets(assets);
            }
        }

        protected internal override void OnStart() => _pool.Take();
    }

    private sealed class Pooled : Entity
    {
        private readonly AudioClip? _clip;

        internal Pooled(TextureHandle texture, AudioClip? clip = null)
            : base(Vector2.Zero)
        {
            _clip = clip;
            Add(new SpriteRenderer(new Sprite(texture, new TextureRegion(0, 0, 1, 1))));
        }

        protected internal override void CollectAssets(AssetCollection assets)
        {
            if (_clip is { } clip)
            {
                assets.Add(clip);
            }
        }
    }
}
