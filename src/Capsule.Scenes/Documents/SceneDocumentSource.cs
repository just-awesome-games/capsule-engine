namespace Capsule.Scenes.Documents;

/// <summary>
/// Where a derived scene document came from: the importer, a forward-slashed relative path, and the lowercase
/// SHA-256 of the source closure. Nothing resolves the path at runtime.
/// </summary>
public readonly record struct SceneDocumentSource(string Tool, string Path, string Hash);
