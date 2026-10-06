using Capsule.Assets;

namespace Capsule.Scenes;

/// <summary>Declarations a <c>CollectAssets</c> hook makes about the entities its scene holds.</summary>
public static class AssetCollectionExtensions
{
    /// <summary>
    /// Declares that the caller takes from its scene's shared pool of <typeparamref name="T"/>, built
    /// with at least <paramref name="capacity"/> entities and preloaded before the scene starts.
    /// </summary>
    /// <remarks>
    /// The scene's pool holds the largest capacity any declaration asks for. A declaration into a
    /// collection no scene gathers builds nothing.
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void CollectAssets(AssetCollection assets) =&gt; assets.Pool&lt;SparkBurst&gt;(capacity: 8);
    /// </code>
    /// </example>
    /// <param name="assets">The collection the caller's hook was handed.</param>
    /// <param name="capacity">How many entities the caller can have taken at once. Positive.</param>
    /// <typeparam name="T">The pooled entity type, which <see cref="Scene.Pool{T}"/> finds.</typeparam>
    public static void Pool<T>(this AssetCollection assets, int capacity)
        where T : Entity, new()
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);

        if (assets.GatheringScene is Scene scene)
        {
            scene.DeclarePool<T>(capacity);
        }
    }
}
