using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>Every texture, sound and scene document the build declared, by the key a document names each by.</summary>
internal sealed class AssetTable
{
    // Each asset type's declared keys, by AssetForm.Type.
    private readonly Dictionary<string, Dictionary<string, string>> _members = PropertyForms.Assets.ToDictionary(
        static form => form.Type, static _ => new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);

    internal AssetTable(ImmutableArray<AssetModel> assets, ImmutableArray<SceneDocumentModel> documents)
    {
        foreach (AssetModel asset in assets.OrderBy(static asset => asset.Key, StringComparer.Ordinal))
        {
            Add(asset.Type, asset.Key, asset.Member);
        }

        foreach (SceneDocumentModel document in documents.OrderBy(static document => document.Key, StringComparer.Ordinal))
        {
            Add(PropertyForms.Assets.First(static form => form.Scene).Type, document.Key, document.Member);
        }
    }

    /// <summary>The texture or sound a member the build marked declares, or null for any other member.</summary>
    internal static AssetModel? Describe(GeneratorAttributeSyntaxContext marked)
    {
        if (marked.TargetSymbol is not IPropertySymbol member
            || marked.Attributes[0].ConstructorArguments is not { Length: 1 } arguments
            || arguments[0].Value is not string key)
        {
            return null;
        }

        // Scene documents come from the documents themselves, never from a marked member.
        return PropertyForms.Asset(SymbolShape.QualifiedName(member.Type)) is { Scene: false } form
            ? new AssetModel(form.Type, key, SymbolShape.QualifiedName(member.ContainingType) + "." + member.Name)
            : null;
    }

    /// <summary>Each declared asset of <paramref name="form"/>: its key, and the member it is read from.</summary>
    internal IReadOnlyDictionary<string, string> Of(AssetForm form) => _members[form.Type];

    // The first member to declare a key keeps it.
    private void Add(string type, string key, string member)
    {
        if (!_members[type].ContainsKey(key))
        {
            _members[type][key] = member;
        }
    }
}
