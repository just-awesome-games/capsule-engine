using System.Text;

namespace Capsule.Build.Sprites;

/// <summary>
/// The rule turning an authored name into the C# name the generated registry declares — the same
/// one the asset registry applies to an asset's file name.
/// </summary>
public static class SpriteSheetNaming
{
    /// <summary>
    /// The identifier <paramref name="name"/> declares, or null when it cannot be one: letters,
    /// digits, <c>-</c> and <c>_</c> only, a separator starting a capitalised word, and never a
    /// leading digit.
    /// </summary>
    public static string? ToIdentifier(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

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
}
