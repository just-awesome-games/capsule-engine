namespace Capsule.Build;

/// <summary>One source the targets hand the build, as a line of the manifest named it.</summary>
/// <param name="Path">Where the source is, relative to the working directory and with forward slashes.</param>
/// <param name="Root">The tree it keys below, as <see cref="Path"/> is spelt: a derivation's output directory, or null for the asset root.</param>
internal readonly record struct Request(string Path, string? Root = null);
