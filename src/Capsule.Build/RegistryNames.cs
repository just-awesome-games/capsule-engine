using Capsule.Generators;

namespace Capsule.Build;

/// <summary>Why <paramref name="identifier"/> is reserved on the class it would be declared on, or null.</summary>
/// <param name="identifier">The identifier a segment of a key became.</param>
/// <param name="leaf">Whether it is the key's last segment, which becomes a member rather than a class.</param>
internal delegate string? RegistryReservation(string identifier, bool leaf);

/// <summary>
/// The names a generated registry has already declared, directory by directory. A source's key
/// becomes a path of nested classes, so every collision C# would refuse is caught here against the
/// source that would have caused it.
/// </summary>
/// <param name="registryClass">The class every key is declared under.</param>
/// <param name="reserved">The names the generated classes take for themselves.</param>
internal sealed class RegistryNames(string registryClass, RegistryReservation reserved)
{
    private readonly Dictionary<string, Dictionary<string, Claim>> _byDirectory = new(StringComparer.Ordinal);

    /// <summary>Declares every class and member <paramref name="key"/> names, from the registry class down.</summary>
    /// <returns>Null when the key was declared, otherwise why it cannot be declared beside what already is.</returns>
    internal string? Declare(string key)
    {
        string[] segments = key.Split('/');
        string enclosing = registryClass;
        string directory = string.Empty;

        for (int i = 0; i < segments.Length; i++)
        {
            bool leaf = i == segments.Length - 1;

            if (TypeNaming.ToIdentifier(segments[i]) is not { } identifier)
            {
                return $"is keyed \"{key}\", whose \"{segments[i]}\" is no C# name; every segment is letters, digits, '-' and '_', and does not start with a digit.";
            }

            if (string.Equals(identifier, enclosing, StringComparison.Ordinal))
            {
                return $"is keyed \"{key}\", whose '{identifier}' would be declared inside a class of that name; name it something else.";
            }

            if (reserved(identifier, leaf) is { } because)
            {
                return $"is keyed \"{key}\", whose '{identifier}' {because}; name it something else.";
            }

            Dictionary<string, Claim> claims = Claims(directory);
            if (claims.TryGetValue(identifier, out Claim claimed))
            {
                // A directory two sources share is one class, not a collision.
                if (leaf || claimed.Leaf)
                {
                    return $"is keyed \"{key}\" and \"{claimed.Key}\" already declares '{identifier}' in the same directory; two names that differ only in their separators are one C# name.";
                }
            }
            else
            {
                claims.Add(identifier, new Claim(key, leaf));
            }

            enclosing = identifier;
            directory = directory + segments[i] + "/";
        }

        return null;
    }

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
