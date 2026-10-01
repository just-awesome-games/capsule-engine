using System.Text;
using Capsule.Assets;

namespace Capsule.Generators;

internal static class TypeNaming
{
    // One leading segment an entity, camera, tile type or baseScene key drops, whichever kind the type
    // is. A shared list keeps every kind's key on the one rule scenes.md states.
    private static readonly string[] DomainSegments = ["Entities", "Cameras", "Tiles", "Scenes"];

    // The key an entity, camera, tile type or baseScene class claims: its namespace under the root, minus a
    // leading domain segment and a trailing segment repeating its own name, kebab-cased per segment
    // and joined with '/'. A type outside the root namespace claims just its kebab-cased name.
    internal static string KeyFor(string containingNamespace, string typeName, string rootNamespace) =>
        KeyFor(containingNamespace, typeName, rootNamespace, dropsDomain: true);

    // The document a scene class claims keeps its leading segment, since its namespace under the root
    // is the document's path under Assets/.
    internal static string DocumentKeyFor(string containingNamespace, string typeName, string rootNamespace) =>
        KeyFor(containingNamespace, typeName, rootNamespace, dropsDomain: false);

    private static string KeyFor(string containingNamespace, string typeName, string rootNamespace, bool dropsDomain)
    {
        string name = AssetPaths.FromTypeName(typeName);
        if (Relative(containingNamespace, rootNamespace) is not { } relative)
        {
            return name;
        }

        int start = dropsDomain && relative.Length > 0 && Array.IndexOf(DomainSegments, relative[0]) >= 0 ? 1 : 0;
        int end = relative.Length;

        // A type in a folder of its own name keys to that folder, not a level below it.
        if (end > start && string.Equals(relative[end - 1], typeName, StringComparison.Ordinal))
        {
            end--;
        }

        if (end <= start)
        {
            return name;
        }

        StringBuilder key = new();
        for (int i = start; i < end; i++)
        {
            key.Append(AssetPaths.FromTypeName(relative[i])).Append('/');
        }

        return key.Append(name).ToString();
    }

    // The namespace segments below the root, or null when the type is not under the root.
    private static string[]? Relative(string containingNamespace, string rootNamespace)
    {
        if (rootNamespace.Length == 0 || containingNamespace.Length == 0)
        {
            return null;
        }

        if (string.Equals(containingNamespace, rootNamespace, StringComparison.Ordinal))
        {
            return [];
        }

        return containingNamespace.Length > rootNamespace.Length
            && containingNamespace[rootNamespace.Length] == '.'
            && containingNamespace.StartsWith(rootNamespace, StringComparison.Ordinal)
                ? containingNamespace.Substring(rootNamespace.Length + 1).Split('.')
                : null;
    }

    // The hash keeps two assembly names that underscore alike apart.
    internal static string RegistryProviderName(string assemblyName)
    {
        uint hash = 2166136261;
        foreach (char character in assemblyName)
        {
            hash ^= character;
            hash *= 16777619;
        }

        return $"CapsuleRegistryProvider_{CodeText.Underscored(assemblyName)}_{hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
