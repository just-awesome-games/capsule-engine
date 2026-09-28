namespace Capsule.Build;

/// <summary>One source after the key pass, which is how everything downstream spells it.</summary>
/// <param name="Key">Its path under <c>Assets/</c> normalized segment by segment, with no extension. It ships at that path.</param>
/// <param name="Extension">The admitted extension in lower case, as a shipped copy and every handle spell it.</param>
/// <param name="Path">Where the source is, relative to the working directory. Every message names it.</param>
internal readonly record struct Source(AssetType Type, string Key, string Extension, string Path);
