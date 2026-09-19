using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Generators;

// One additional file plus the two pieces of metadata the asset hook wrote beside it. The options
// provider never compares equal, so it is projected away as soon as a file arrives and the pipeline
// can then cache a font parse across passes.
internal readonly struct AssetFile(AdditionalText text, string? domain, string authored) : IEquatable<AssetFile>
{
    private const string DomainMetadata = "build_metadata.AdditionalFiles.CapsuleAssetDomain";

    private const string PathMetadata = "build_metadata.AdditionalFiles.CapsuleAssetPath";

    internal AdditionalText Text { get; } = text;

    /// <summary>The domain root the file was authored under. Null when the hook did not write it.</summary>
    internal string? Domain { get; } = domain;

    /// <summary>The path the hook authored it at, extension stripped, forward slashes only.</summary>
    internal string Authored { get; } = authored;

    internal static AssetFile From(AdditionalText text, AnalyzerConfigOptionsProvider options)
    {
        AnalyzerConfigOptions declared = options.GetOptions(text);

        // MSBuild's %(RecursiveDir) carries the platform separator. Handles use forward slashes.
        string authored = declared.TryGetValue(PathMetadata, out string? path) && !string.IsNullOrEmpty(path)
            ? path!.Replace('\\', '/')
            : System.IO.Path.GetFileNameWithoutExtension(text.Path);

        return new AssetFile(text, declared.TryGetValue(DomainMetadata, out string? domain) ? domain : null, authored);
    }

    /// <summary>Whether the domain this file declares is <paramref name="domain"/>.</summary>
    internal bool InDomain(string domain) => string.Equals(Domain, domain, StringComparison.Ordinal);

    public bool Equals(AssetFile other) =>
        ReferenceEquals(Text, other.Text)
        && string.Equals(Domain, other.Domain, StringComparison.Ordinal)
        && string.Equals(Authored, other.Authored, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AssetFile other && Equals(other);

    public override int GetHashCode() => (Text.Path.GetHashCode() * 31) + Authored.GetHashCode();
}
