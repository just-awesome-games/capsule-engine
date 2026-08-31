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

    internal static bool IsSafeDocumentName(string documentName)
    {
        if (documentName.Length == 0)
        {
            return false;
        }

        foreach (char character in documentName)
        {
            bool safe = character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-'
                or '_';
            if (!safe)
            {
                return false;
            }
        }

        return true;
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
