using System.Text;

namespace Capsule.Scenes.Generator;

/// <summary>The convention that turns a class name into the level type it claims.</summary>
internal static class LevelTypeId
{
    /// <summary>
    /// Kebab-cases <paramref name="typeName"/>: a boundary falls before an upper-case letter
    /// that starts a word, so <c>HealthPickup</c> gives <c>health-pickup</c> and an acronym
    /// stays whole — <c>HttpProbe</c> and <c>HTTPProbe</c> both give <c>http-probe</c>.
    /// </summary>
    internal static string FromTypeName(string typeName)
    {
        StringBuilder id = new(typeName.Length + 4);

        for (int i = 0; i < typeName.Length; i++)
        {
            char character = typeName[i];
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
}
