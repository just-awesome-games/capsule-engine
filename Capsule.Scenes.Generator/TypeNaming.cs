using System.Text;

namespace Capsule.Scenes.Generator;

/// <summary>The conventions that turn a class name into the name it claims, and back.</summary>
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

    /// <summary>
    /// The inverse convention, for a name a file carries rather than a class: <c>footstep-stone</c>
    /// gives <c>FootstepStone</c>, and an inner capital survives, so <c>fooBar</c> gives
    /// <c>FooBar</c>. Underscores separate words like hyphens, which is what makes
    /// <c>a-b</c> and <c>a_b</c> one name and therefore a collision rather than two members
    /// differing invisibly. Null where no identifier can come out — anything outside ASCII
    /// letters, digits, hyphens and underscores, or a leading digit.
    /// </summary>
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
