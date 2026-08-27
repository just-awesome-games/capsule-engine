namespace Capsule.Scenes.Generator;

internal enum AssetFault
{
    None,
    UnsafeName,
    DomainCollision,
}

/// <summary>
/// One shipped asset as the pipeline carries it. A path is flattened to the names generated code
/// and a diagnostic need: the file itself is never read, so an asset's bytes changing re-ships it
/// without recompiling anything.
/// </summary>
internal readonly struct AssetModel : IEquatable<AssetModel>
{
    internal AssetModel(
        string domain,
        string name,
        string extension,
        string fileName,
        string identifier,
        AssetFault fault)
    {
        Domain = domain;
        Name = name;
        Extension = extension;
        FileName = fileName;
        Identifier = identifier;
        Fault = fault;
    }

    /// <summary>The domain root it was authored under, which is also where it ships.</summary>
    internal string Domain { get; }

    /// <summary>The file stem, which is half of what a handle carries.</summary>
    internal string Name { get; }

    /// <summary>The extension with its dot, which is the other half.</summary>
    internal string Extension { get; }

    /// <summary>The file as a diagnostic and a doc comment name it.</summary>
    internal string FileName { get; }

    /// <summary>The member generated code declares, or empty where the name cannot become one.</summary>
    internal string Identifier { get; }

    internal AssetFault Fault { get; }

    public static bool operator ==(AssetModel left, AssetModel right) => left.Equals(right);

    public static bool operator !=(AssetModel left, AssetModel right) => !left.Equals(right);

    public bool Equals(AssetModel other) =>
        Fault == other.Fault
        && string.Equals(Domain, other.Domain, StringComparison.Ordinal)
        && string.Equals(FileName, other.FileName, StringComparison.Ordinal)
        && string.Equals(Identifier, other.Identifier, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AssetModel other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        hash = (hash * 31) + Domain.GetHashCode();
        hash = (hash * 31) + FileName.GetHashCode();
        hash = (hash * 31) + Identifier.GetHashCode();
        hash = (hash * 31) + (int)Fault;

        return hash;
    }
}
