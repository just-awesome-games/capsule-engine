namespace Capsule.Scenes.Documents;

/// <summary>
/// Provenance for a derived scene document: importer, forward-slashed relative path, and lowercase
/// SHA-256 of the source closure. Nothing resolves the path at runtime.
/// </summary>
public readonly record struct SceneDocumentSource(string Tool, string Path, string Hash);
