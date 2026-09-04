namespace Capsule.Generators;

internal enum AssetFault
{
    None,
    UnsafeName,
}

internal readonly struct AssetModel : IEquatable<AssetModel>
{
    internal AssetModel(string domain, string path, string extension, AssetFault fault)
    {
        Domain = domain;
        Path = path;
        Extension = extension;
        Fault = fault;
    }

    internal string Domain { get; }

    /// <summary>The source's path under its domain root, extension stripped, forward slashes only.</summary>
    internal string Path { get; }

    internal string Extension { get; }

    internal AssetFault Fault { get; }

    /// <summary>What a diagnostic names the asset by: its path under the source tree.</summary>
    internal string Display => Domain + "/" + Path + Extension;

    public bool Equals(AssetModel other) =>
        Fault == other.Fault
        && string.Equals(Domain, other.Domain, StringComparison.Ordinal)
        && string.Equals(Path, other.Path, StringComparison.Ordinal)
        && string.Equals(Extension, other.Extension, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AssetModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + Domain.GetHashCode();
        hash = (hash * 31) + Path.GetHashCode();
        hash = (hash * 31) + Extension.GetHashCode();
        hash = (hash * 31) + (int)Fault;

        return hash;
    }
}
