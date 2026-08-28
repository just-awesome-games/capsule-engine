namespace Capsule.Scenes.Generator;

internal enum AssetFault
{
    None,
    UnsafeName,
    DomainCollision,
}

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

    internal string Domain { get; }

    internal string Name { get; }

    internal string Extension { get; }

    internal string FileName { get; }

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
