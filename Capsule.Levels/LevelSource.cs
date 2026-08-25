namespace Capsule.Levels;

/// <summary>
/// Where a generated level came from. Its presence means the level file is an artifact: edit
/// the source and re-import, never the file. The path is the source path as the importer
/// received it, forward-slashed — shell-project-relative when the build hook produced it, and
/// provenance only: nothing resolves it at runtime. The hash is the lowercase hex SHA-256 of
/// the source bytes.
/// </summary>
public readonly record struct LevelSource(string Tool, string Path, string Hash);
