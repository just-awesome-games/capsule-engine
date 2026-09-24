using System.Text;
using Capsule.Generators;

namespace Capsule.Build.Registry;

/// <summary>Writes one member of a folder's class at <paramref name="indent"/>, under <paramref name="identifier"/>.</summary>
internal delegate void MemberWriter(StringBuilder source, string indent, string identifier);

/// <summary>
/// One folder under <c>Assets/</c> as a generated class: the members its files declare, and a nested
/// class per folder inside it.
/// </summary>
/// <param name="identifier">The class's name.</param>
/// <param name="key">The folder's key, empty at the root, ending in a separator below it.</param>
internal sealed class RegistryFolder(string identifier, string key)
{
    private readonly string _key = key;

    private readonly SortedDictionary<string, MemberWriter> _members = new(StringComparer.Ordinal);

    private readonly SortedDictionary<string, RegistryFolder> _folders = new(StringComparer.Ordinal);

    // What already declares each identifier here: a folder's key, or the path of the file that took it.
    private readonly Dictionary<string, string> _claimedBy = new(StringComparer.Ordinal);

    /// <summary>
    /// Declares a member named <paramref name="name"/> in the folder <paramref name="key"/> sits in,
    /// creating the folder classes it walks through.
    /// </summary>
    /// <param name="key">The file's key, forward slashes and no extension.</param>
    /// <param name="name">What the member is called after its file's name.</param>
    /// <param name="display">The file's path, as a refusal names it.</param>
    /// <returns>Null when declared, else why C# would refuse it, as a clause following the key.</returns>
    internal string? Add(string key, string name, string display, MemberWriter write)
    {
        RegistryFolder folder = this;
        string[] segments = key.Split('/');

        for (int i = 0; i < segments.Length - 1; i++)
        {
            string identifier = TypeNaming.ToIdentifier(segments[i])!;
            string nested = folder._key + segments[i] + "/";

            if (folder.Refused(identifier, nested) is { } because)
            {
                return because;
            }

            if (!folder._folders.TryGetValue(identifier, out RegistryFolder? child))
            {
                child = new RegistryFolder(identifier, nested);
                folder._folders.Add(identifier, child);
                folder._claimedBy.Add(identifier, nested);
            }

            folder = child;
        }

        if (folder.Refused(name, display) is { } refused)
        {
            return refused;
        }

        folder._members.Add(name, write);
        folder._claimedBy.Add(name, display);

        return null;
    }

    /// <summary>Writes this class at <paramref name="indent"/>, every nested class included.</summary>
    internal void Append(StringBuilder source, string indent, string summary)
    {
        source.Append(indent).Append("/// <summary>").Append(summary).AppendLine("</summary>");
        source.Append(indent).AppendLine("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        source.Append(indent).Append("public static class ").AppendLine(identifier);
        source.Append(indent).AppendLine("{");

        string inner = indent + "    ";
        bool first = true;

        foreach ((string name, MemberWriter write) in _members)
        {
            if (!first)
            {
                source.AppendLine();
            }

            first = false;
            write(source, inner, name);
        }

        foreach (RegistryFolder folder in _folders.Values)
        {
            if (!first)
            {
                source.AppendLine();
            }

            first = false;
            folder.Append(source, inner, $"Every asset authored under <c>{folder._key[..^1]}</c>.");
        }

        source.Append(indent).AppendLine("}");
    }

    private string? Refused(string name, string display)
    {
        if (string.Equals(name, identifier, StringComparison.Ordinal))
        {
            return $"whose '{name}' would be declared inside a generated class of that name. Rename the file or its folder.";
        }

        // A folder two files share is one class, not a collision.
        return _claimedBy.TryGetValue(name, out string? claimed)
            && !(_folders.ContainsKey(name) && string.Equals(claimed, display, StringComparison.Ordinal))
                ? $"whose '{name}' is already declared in that folder by \"{claimed}\". Two names differing only in their separators are one C# name."
                : null;
    }
}
