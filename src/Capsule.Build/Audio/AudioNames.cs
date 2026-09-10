using Capsule.Generators;

namespace Capsule.Build.Audio;

/// <summary>
/// The names the generated audio registry has already declared, directory by directory. A clip's
/// key becomes a path of nested classes, so every collision C# would refuse is caught here against
/// the source that would have caused it.
/// </summary>
internal sealed class AudioNames
{
    private readonly Dictionary<string, Dictionary<string, Claim>> _byDirectory = new(StringComparer.Ordinal);

    /// <summary>Declares every class and member <paramref name="key"/> names, from the registry class down.</summary>
    /// <exception cref="AudioFormatException">The key cannot be declared beside what already is.</exception>
    internal void Declare(string key)
    {
        string[] segments = key.Split('/');
        string enclosing = AudioRegistrySource.RegistryClass;
        string directory = string.Empty;

        for (int i = 0; i < segments.Length; i++)
        {
            bool leaf = i == segments.Length - 1;
            string identifier = Identifier(segments[i], key);

            if (string.Equals(identifier, enclosing, StringComparison.Ordinal))
            {
                throw new AudioFormatException(
                    $"is keyed \"{key}\", whose '{identifier}' would be declared inside a class of that name; name it something else.");
            }

            if (string.Equals(identifier, AudioRegistrySource.ListMember, StringComparison.Ordinal))
            {
                throw new AudioFormatException(
                    $"is keyed \"{key}\", whose '{identifier}' is the set member every generated class declares; name it something else.");
            }

            Dictionary<string, Claim> claims = Claims(directory);
            if (claims.TryGetValue(identifier, out Claim claimed))
            {
                // A directory two clips share is one class, not a collision.
                if (leaf || claimed.Leaf)
                {
                    throw new AudioFormatException(
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
        TypeNaming.ToIdentifier(segment)
        ?? throw new AudioFormatException(
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
