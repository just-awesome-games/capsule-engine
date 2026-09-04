namespace Capsule.Build.Sprites;

/// <summary>
/// The names the generated sheet registry has already declared, directory by directory. A sheet's
/// key becomes a path of nested classes, so every collision C# would refuse — a member carrying the
/// name of the class it is declared on, two siblings of one name — is caught here against the
/// source that would have caused it.
/// </summary>
internal sealed class SheetNames
{
    private readonly Dictionary<string, Dictionary<string, Claim>> _byDirectory = new(StringComparer.Ordinal);

    /// <summary>Declares every class <paramref name="key"/> names, from the registry class down.</summary>
    /// <exception cref="SpriteSheetFormatException">The key cannot be declared beside what already is.</exception>
    internal void Declare(string key)
    {
        string[] segments = key.Split('/');
        string enclosing = SpriteRegistrySource.RegistryClass;
        string directory = string.Empty;

        for (int i = 0; i < segments.Length; i++)
        {
            bool leaf = i == segments.Length - 1;
            string identifier = Identifier(segments[i], key);

            if (string.Equals(identifier, enclosing, StringComparison.Ordinal))
            {
                throw new SpriteSheetFormatException(
                    $"is keyed \"{key}\", whose '{identifier}' would be declared inside a class of that name; name it something else.");
            }

            // A sheet declares Frames and Clips inside its own class; a directory declares neither.
            if (leaf && identifier is SpriteRegistrySource.FramesClass or SpriteRegistrySource.ClipsClass)
            {
                throw new SpriteSheetFormatException(
                    $"is keyed \"{key}\", whose '{identifier}' is one of the generated classes a sheet declares ('{SpriteRegistrySource.FramesClass}', '{SpriteRegistrySource.ClipsClass}'); name it something else.");
            }

            Dictionary<string, Claim> claims = Claims(directory);
            if (claims.TryGetValue(identifier, out Claim claimed))
            {
                // A directory two sheets share is one class, not a collision.
                if (leaf || claimed.Leaf)
                {
                    throw new SpriteSheetFormatException(
                        $"is keyed \"{key}\" and \"{claimed.Key}\" already declares '{identifier}' in the same directory; two names that differ only in their separators are one C# name.");
                }
            }
            else
            {
                claims.Add(identifier, new Claim(key, leaf));
            }

            enclosing = identifier;
            directory = directory + segments[i] + "/";
        }
    }

    private static string Identifier(string segment, string key) =>
        SpriteSheetNaming.ToIdentifier(segment)
        ?? throw new SpriteSheetFormatException(
            $"is keyed \"{key}\", whose \"{segment}\" is no C# name; every segment is letters, digits, '-' and '_', and does not start with a digit.");

    private Dictionary<string, Claim> Claims(string directory)
    {
        if (!_byDirectory.TryGetValue(directory, out Dictionary<string, Claim>? claims))
        {
            claims = new Dictionary<string, Claim>(StringComparer.Ordinal);
            _byDirectory.Add(directory, claims);
        }

        return claims;
    }

    private readonly record struct Claim(string Key, bool Leaf);
}
