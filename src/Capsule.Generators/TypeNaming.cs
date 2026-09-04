using System.Text;

namespace Capsule.Generators;

internal static class TypeNaming
{
    internal static string FromTypeName(string typeName)
    {
        StringBuilder id = new(typeName.Length + 4);

        for (int i = 0; i < typeName.Length; i++)
        {
            char character = typeName[i];

            if (char.IsDigit(character))
            {
                if (i > 0 && char.IsLetter(typeName[i - 1]))
                {
                    id.Append('-');
                }

                id.Append(character);
                continue;
            }

            if (!char.IsUpper(character))
            {
                id.Append(character);
                continue;
            }

            bool startsWord = i > 0
                && (!char.IsUpper(typeName[i - 1]) || (i + 1 < typeName.Length && char.IsLower(typeName[i + 1])));
            if (startsWord)
            {
                id.Append('-');
            }

            id.Append(char.ToLowerInvariant(character));
        }

        return id.ToString();
    }

    internal static string? ToIdentifier(string name)
    {
        StringBuilder identifier = new(name.Length);
        bool startOfWord = true;

        foreach (char character in name)
        {
            if (character is '-' or '_')
            {
                startOfWord = true;
                continue;
            }

            bool legal = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
            if (!legal)
            {
                return null;
            }

            identifier.Append(startOfWord ? char.ToUpperInvariant(character) : character);
            startOfWord = false;
        }

        return identifier.Length > 0 && !char.IsDigit(identifier[0]) ? identifier.ToString() : null;
    }

    // The key a type claims: its namespace under the root, minus a leading domain segment and a
    // trailing segment repeating its own name, kebab-cased per segment and joined with '/'. A type
    // outside the root namespace claims its kebab-cased name alone.
    internal static string KeyFor(string containingNamespace, string typeName, string rootNamespace, string domainSegment)
    {
        string name = FromTypeName(typeName);
        if (Relative(containingNamespace, rootNamespace) is not { } relative)
        {
            return name;
        }

        int start = relative.Length > 0 && string.Equals(relative[0], domainSegment, StringComparison.Ordinal) ? 1 : 0;
        int end = relative.Length;

        // A type in a folder of its own name is that folder, not a level below it.
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
            key.Append(FromTypeName(relative[i])).Append('/');
        }

        return key.Append(name).ToString();
    }

    // The namespace segments below the root, or null when the type is not under it at all.
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

    internal static string RegistryProviderName(string assemblyName)
    {
        StringBuilder identifier = new("CapsuleRegistryProvider_");
        foreach (char character in assemblyName)
        {
            identifier.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        uint hash = 2166136261;
        foreach (char character in assemblyName)
        {
            hash ^= character;
            hash *= 16777619;
        }

        identifier.Append('_');
        identifier.Append(hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));

        return identifier.ToString();
    }
}
