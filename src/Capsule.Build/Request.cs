namespace Capsule.Build;

/// <summary>One source the build reads, as the walk found it or an importer wrote it.</summary>
/// <param name="Path">Where the source is, relative to the working directory and with forward slashes.</param>
/// <param name="Length">Its size in bytes when the run found it.</param>
/// <param name="Written">Its last write time, UTC, when the run found it.</param>
/// <param name="Root">The tree it keys below, as <see cref="Path"/> is spelt: the directory importers write to, or null for the asset root.</param>
/// <param name="Sha256">Its content hash when the run wrote it and hashed the bytes it wrote, or null.</param>
internal readonly record struct Request(string Path, long Length, DateTime Written, string? Root = null, string? Sha256 = null);
