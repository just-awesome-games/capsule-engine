using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>One texture or sound the build declared on <c>CapsuleAssets</c>, as its <c>CapsuleGeneratedAsset</c> attribute marks it.</summary>
/// <param name="Type">The <see cref="AssetForm.Type"/> of its entry in <see cref="PropertySchema.Assets"/>.</param>
/// <param name="Key">Its key and extension, as a document names it once normalized: <c>textures/hazard.png</c>.</param>
/// <param name="Member">The fully qualified member generated code reads it from.</param>
internal readonly record struct AssetModel(string Type, string Key, string Member)
{
    /// <summary>What the build marks each texture and sound member with.</summary>
    internal const string Attribute = "Capsule.Generated.CapsuleGeneratedAssetAttribute";

    internal static AssetModel? From(GeneratorAttributeSyntaxContext marked)
    {
        if (marked.TargetSymbol is not IPropertySymbol member
            || marked.Attributes[0].ConstructorArguments is not { Length: 1 } arguments
            || arguments[0].Value is not string key)
        {
            return null;
        }

        // Scene documents come from the documents themselves, never from a marked member.
        return PropertySchema.Asset(member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) is { Scene: false } form
            ? new AssetModel(form.Type, key, member.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + member.Name)
            : null;
    }
}

/// <summary>Every texture, sound and scene document the build declared, by the key a document names each by.</summary>
internal sealed class AssetTable
{
    // Each asset type's declared keys, by AssetForm.Type.
    private readonly Dictionary<string, Dictionary<string, string>> _members = PropertySchema.Assets.ToDictionary(
        static form => form.Type, static _ => new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);

    internal AssetTable(ImmutableArray<AssetModel> assets, ImmutableArray<SceneDocumentInfo> documents)
    {
        foreach (AssetModel asset in assets.OrderBy(static asset => asset.Key, StringComparer.Ordinal))
        {
            Add(asset.Type, asset.Key, asset.Member);
        }

        foreach (SceneDocumentInfo document in documents.OrderBy(static document => document.Key, StringComparer.Ordinal))
        {
            Add(PropertySchema.Assets.First(static form => form.Scene).Type, document.Key, document.Member);
        }
    }

    // The first member to declare a key keeps it.
    private void Add(string type, string key, string member)
    {
        if (!_members[type].ContainsKey(key))
        {
            _members[type][key] = member;
        }
    }

    /// <summary>Each declared asset of <paramref name="form"/>: its key, and the member it is read from.</summary>
    internal IReadOnlyDictionary<string, string> Of(AssetForm form) => _members[form.Type];
}
