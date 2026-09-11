using Capsule.Assets;

namespace Capsule.Build;

/// <summary>
/// One authored document and the key it claims: its path under its root, forward slashes and no
/// extensions — <c>enemies/bat</c>, or <c>bat</c> for a document at the root. The key is where the
/// derived document is written and what the generated registry declares it as.
/// </summary>
/// <param name="Key">The document's root-relative key.</param>
/// <param name="Path">Where the source is, relative to the working directory.</param>
internal readonly record struct DocumentSource(string Key, string Path)
{
    /// <summary>
    /// Whether <see cref="Key"/> is a key at all. A key arriving from an authoring module is input
    /// like any other, and one that is no key would write outside the directory the build owns.
    /// </summary>
    internal bool HasSafeKey() => AssetPaths.IsKey(Key);
}
