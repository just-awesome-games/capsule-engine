using System.Numerics;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

[Collection(LogSinkCollection.Name)]
public sealed class EntityPoolDiagnosticsTests : IDisposable
{
    private static readonly TextureHandle ShotTexture = new("pooled/shot", ".png");
    private static readonly AudioClip Streamed = new("pooled/hum", ".ogg", 4d);

    // Each setup takes from a pool and returns what the first-take line names that pool by.
    public static TheoryData<Func<string>, int> FirstTakes => new()
    {
        { UnforwardedTakenTwice, 1 },
        { Forwarded, 0 },
        { WhiteTexelOnly, 0 },
        { StreamedClipOnly, 0 },
        { ReadPreloadsThenTaken, 1 },
        { () => HeadlessAfterARead(forwarded: true), 0 },
        { () => HeadlessAfterARead(forwarded: false), 1 },
        { () => ScenePoolTakenTwice(declared: true), 0 },
        { () => ScenePoolTakenTwice(declared: false), 1 },
    };

    public void Dispose() => Log.UseSink(null);

    // A pool nothing forwarded logs its type at Info on its first take, once. A forwarded pool, one holding
    // nothing to preload and a scene pool a declaration reached stay quiet.
    [Theory]
    [MemberData(nameof(FirstTakes))]
    public void APoolsFirstTake_LogsOnceOnlyWhenNothingForwardedAPoolHoldingAPreload(Func<string> takes, int logged)
    {
        CollectingLogSink sink = new();
        Log.UseSink(sink);

        string pool = takes();

        LogEntry[] lines = [.. sink.Entries.Where(entry => entry.Message.Contains(pool, StringComparison.Ordinal))];
        Assert.Equal(logged, lines.Length);
        Assert.All(lines, static line => Assert.Equal(LogLevel.Info, line.Level));
    }

    private static string UnforwardedTakenTwice()
    {
        EntityPool<Pooled> pool = new(static () => new Pooled(ShotTexture), capacity: 2);
        pool.Take();
        pool.Take();

        return "EntityPool<Pooled>";
    }

    private static string Forwarded()
    {
        EntityPool<Pooled> pool = new(static () => new Pooled(ShotTexture), capacity: 1);
        pool.CollectAssets(new AssetCollection());
        pool.Take();

        return "EntityPool<Pooled>";
    }

    private static string WhiteTexelOnly()
    {
        new EntityPool<Pooled>(static () => new Pooled(TextureHandle.White), capacity: 1).Take();

        return "EntityPool<Pooled>";
    }

    private static string StreamedClipOnly()
    {
        new EntityPool<Pooled>(static () => new Pooled(TextureHandle.White, Streamed), capacity: 1).Take();

        return "EntityPool<Pooled>";
    }

    // Reading a scene's preloads forwards no pool.
    private static string ReadPreloadsThenTaken()
    {
        EntityPool<Pooled> pool = new(static () => new Pooled(ShotTexture), capacity: 1);
        Scene scene = new();
        scene.Add(new Forwarder(pool));
        Assert.True(scene.CollectPreloads().Contains(ShotTexture));
        pool.Take();

        return "EntityPool<Pooled>";
    }

    // A headless host collects before the scene starts even after a read, which forwards the owner's pool.
    private static string HeadlessAfterARead(bool forwarded)
    {
        Scene scene = new();
        scene.Add(new PoolOwner(forwarded));
        scene.CollectPreloads();
        using SimulationHost host = new(scene);

        return "EntityPool<Pooled>";
    }

    private static string ScenePoolTakenTwice(bool declared)
    {
        Scene scene = new();
        if (declared)
        {
            scene.Add(new Declarer());
        }

        using SimulationHost host = new(scene);
        scene.Add(scene.Pool<ScenePooled>().Take());
        scene.Add(scene.Pool<ScenePooled>().Take());

        return "assets.Pool<ScenePooled>";
    }

    private sealed class Forwarder(EntityPool<Pooled> pool) : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => pool.CollectAssets(assets);
    }

    // Takes in its start hook, the earliest a game can.
    private sealed class PoolOwner(bool forwards) : Entity(Vector2.Zero)
    {
        private readonly EntityPool<Pooled> _pool = new(() => new Pooled(ShotTexture), capacity: 1);

        protected internal override void CollectAssets(AssetCollection assets)
        {
            if (forwards)
            {
                _pool.CollectAssets(assets);
            }
        }

        protected internal override void OnStart() => _pool.Take();
    }

    private sealed class Declarer() : Entity(Vector2.Zero)
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<ScenePooled>(capacity: 1);
    }

    private sealed class ScenePooled : Entity
    {
        public ScenePooled()
            : base(Vector2.Zero) => Add(new SpriteRenderer(new Sprite(ShotTexture, new TextureRegion(0, 0, 1, 1))));
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
