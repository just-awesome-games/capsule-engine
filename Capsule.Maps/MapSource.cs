namespace Capsule.Maps;

/// <summary>
/// Provenance for a derived map: importer, forward-slashed relative path, and lowercase SHA-256
/// of the complete source closure. Nothing resolves the path at runtime.
/// </summary>
public readonly record struct MapSource(string Tool, string Path, string Hash);
