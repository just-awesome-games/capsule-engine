namespace Capsule.Build;

/// <summary>
/// One authored document and the key the key pass derived for it: its path under its root, forward
/// slashes and no extensions, as <c>enemies/bat</c> or <c>bat</c> for a document at the root. The
/// derived document is written at that key, and the generated registry declares it under that key.
/// </summary>
/// <param name="Key">The document's root-relative key.</param>
/// <param name="Path">Where the source is, relative to the working directory.</param>
internal readonly record struct DocumentSource(string Key, string Path);
