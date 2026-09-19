namespace Capsule.Generators;

internal enum AssetFault
{
    None,
    UnsafeName,
}

internal readonly struct AssetModel(
    string domain,
    string path,
    string authored,
    string extension,
    string source,
    AssetFault fault)
    : IEquatable<AssetModel>
{
    internal string Domain { get; } = domain;

    /// <summary>The source's key under its domain root, extension stripped, forward slashes only.</summary>
    internal string Path { get; } = path;

    /// <summary>The path as the game spelled it. A diagnostic uses it to point back at the file.</summary>
    internal string Authored { get; } = authored;

    internal string Extension { get; } = extension;

    /// <summary>The file on disk a build error navigates to.</summary>
    internal string Source { get; } = source;

    internal AssetFault Fault { get; } = fault;

    /// <summary>The asset's path under the source tree, as a diagnostic names it.</summary>
    internal string Display => Domain + "/" + Authored + Extension;

    /// <summary>The path the build ships the asset to, keyed under its domain root.</summary>
    internal string Shipped => Domain + "/" + Path + Extension;

    public bool Equals(AssetModel other) =>
        Fault == other.Fault
        && string.Equals(Domain, other.Domain, StringComparison.Ordinal)
        && string.Equals(Path, other.Path, StringComparison.Ordinal)
        && string.Equals(Authored, other.Authored, StringComparison.Ordinal)
        && string.Equals(Extension, other.Extension, StringComparison.Ordinal)
        && string.Equals(Source, other.Source, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AssetModel other && Equals(other);

    public override int GetHashCode() =>
        (Path.GetHashCode() * 31) ^ (Extension.GetHashCode() * 17) ^ (int)Fault;
}
