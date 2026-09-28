namespace Capsule.Build.Atlases;

/// <summary>One atlas a <c>&lt;name&gt;.atlas.json</c> declares.</summary>
/// <param name="Path">The file that declares it.</param>
/// <param name="Config">Its settings, or null when the file is defective and already failed the pass.</param>
internal readonly record struct DeclaredAtlas(string Path, AtlasConfigJson? Config);
