namespace Capsule.Levels;

/// <summary>
/// Where a generated level came from. Its presence means the level file is an artifact: edit
/// the source and re-import, never the file. The path is relative to the level file's own
/// directory, with forward slashes; the hash is the lowercase hex SHA-256 of the source bytes.
/// </summary>
public readonly record struct LevelSource(string Tool, string Path, string Hash);
