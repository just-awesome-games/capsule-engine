using Capsule.Assets;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

public partial class Scene
{
    /// <summary>
    /// The pool of <typeparamref name="T"/> that every spawner in this scene shares. The scene builds it
    /// from its declarations, preloads it before it starts, and returns its entities when they leave or
    /// the scene stops.
    /// </summary>
    /// <remarks>
    /// What takes from the pool declares it with <see cref="AssetCollectionExtensions.Pool{T}"/> from its
    /// <c>CollectAssets</c>, which states how declarations add up. A pool no declaration reached builds
    /// one entity at its first call, and its first take logs once at <see cref="Log.Info"/> that its
    /// assets were not preloaded.
    /// <para>
    /// A taken entity is a new life. Everything per-life, tuning included, is set after <c>Take</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void CollectAssets(AssetCollection assets) =&gt; assets.Pool&lt;SparkBurst&gt;(capacity: 8);
    ///
    /// protected override void OnStep(in StepContext context)
    /// {
    ///     if (_life.IsRunning) return;
    ///     Scene.Add(Scene.Pool&lt;SparkBurst&gt;().Take().Burst(Position));
    ///     Scene.Remove(this);
    /// }
    /// </code>
    /// </example>
    /// <typeparam name="T">The pooled entity type, built with no arguments.</typeparam>
    public EntityPool<T> Pool<T>()
        where T : Entity, new() => Pools.Get<T>();

    /// <summary>
    /// Returns a new collection of every asset this scene and its entities declare now, as the scene
    /// boundary would preload them.
    /// </summary>
    /// <remarks>
    /// A scene read before it starts reports what its boundary loads. The read runs the
    /// <c>CollectAssets</c> hooks and loads no asset. It builds the shared pools those hooks declare,
    /// entities included, and counts as no pool's forwarding.
    /// </remarks>
    /// <example>
    /// <code>
    /// Assert.True(new Room().CollectPreloads().Contains(CapsuleAssets.Textures.RippleTexture));
    /// </code>
    /// </example>
    public AssetCollection CollectPreloads() => Pools.Collect(new AssetCollection { IsProbe = true });

    // Collects the preload the scene boundary loads, which sizes the scene's pools and its growth.
    internal AssetCollection CollectAssetPreloads() => Pools.Collect(new AssetCollection());
}
