using System.Text;

namespace Capsule.Scenes.Generator;

/// <summary>The convention that turns a class name into the name it claims.</summary>
internal static class TypeNaming
{
    /// <summary>
    /// Kebab-cases <paramref name="typeName"/>: a boundary falls before an upper-case letter
    /// that starts a word, so <c>HealthPickup</c> gives <c>health-pickup</c> and an acronym
    /// stays whole — <c>HttpProbe</c> and <c>HTTPProbe</c> both give <c>http-probe</c>. A run of
    /// digits is a word of its own, so <c>Room01</c> gives <c>room-01</c>.
    /// </summary>
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

    internal static bool IsSafeMapName(string mapName)
    {
        if (mapName.Length == 0)
        {
            return false;
        }

        foreach (char character in mapName)
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
