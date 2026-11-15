namespace Capsule.Assets;

/// <summary>
/// Stops a scene, entity or component class from preloading the <c>CapsuleAssets</c> members its own code reads,
/// for a class that names many assets and shows one. Its <c>CollectAssets</c> declares what it needs.
/// </summary>
/// <remarks>
/// It covers only the class it marks. A subclass preloads what its own code reads, and the marked class's base
/// classes preload theirs.
/// </remarks>
/// <example>
/// <code>
/// [ManualPreloads]
/// public sealed class Gallery : Scene
/// {
///     protected override void CollectAssets(AssetCollection assets) =&gt; assets.Add(Portraits[Shown]);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ManualPreloadsAttribute : Attribute;
