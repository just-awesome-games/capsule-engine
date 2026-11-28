using System.Text;

namespace Capsule.Build.Registry;

/// <summary>
/// A generated class and the classes nested in it, which C# lets declare each name once: a member, or a
/// nested class every path through it shares.
/// </summary>
/// <typeparam name="T">What writes a member.</typeparam>
/// <param name="identifier">The class's name.</param>
/// <param name="key">What names the class in a refusal and its summary, empty at the root.</param>
internal sealed class NameTree<T>(string identifier, string key)
{
    private readonly string _identifier = identifier;

    private readonly string _key = key;

    // Every member and nested class, in the order each was first declared.
    private readonly List<(string Name, T Member, NameTree<T>? Nested)> _entries = [];

    private readonly Dictionary<string, NameTree<T>> _nested = new(StringComparer.Ordinal);

    // What takes each name here: a nested class's key, or the claimant of the member.
    private readonly Dictionary<string, string> _claimedBy = new(StringComparer.Ordinal);

    /// <summary>
    /// Declares <paramref name="member"/> as <paramref name="name"/> in the class <paramref name="classes"/> nests
    /// down to, creating each class it walks through.
    /// </summary>
    /// <param name="classes">Each nested class's identifier and key, outermost first.</param>
    /// <param name="claimant">What names the member in a refusal.</param>
    /// <returns>
    /// Null when declared, else the name C# would refuse and what already takes it, or a null claimant where
    /// the name is the class's own.
    /// </returns>
    internal (string Name, string? Claimant)? Add(IEnumerable<(string Identifier, string Key)> classes, string name, string claimant, T member)
    {
        NameTree<T> tree = this;
        foreach ((string nested, string nestedKey) in classes)
        {
            if (tree.Refused(nested) is { } refused
                && !(tree._nested.ContainsKey(nested) && string.Equals(refused.Claimant, nestedKey, StringComparison.Ordinal)))
            {
                return refused;
            }

            if (!tree._nested.TryGetValue(nested, out NameTree<T>? child))
            {
                child = new NameTree<T>(nested, nestedKey);
                tree._nested.Add(nested, child);
                tree._claimedBy.Add(nested, nestedKey);
                tree._entries.Add((nested, default!, child));
            }

            tree = child;
        }

        if (tree.Refused(name) is { } taken)
        {
            return taken;
        }

        tree._claimedBy.Add(name, claimant);
        tree._entries.Add((name, member, null));

        return null;
    }

    /// <summary>Writes every member and nested class at <paramref name="indent"/>, in the order each was first declared.</summary>
    /// <param name="summary">A nested class's summary, from its key.</param>
    /// <param name="write">Writes one member at an indent, under its name.</param>
    internal void AppendMembers(StringBuilder code, string indent, Func<string, string> summary, Action<StringBuilder, string, string, T> write)
    {
        bool first = true;
        foreach ((string name, T member, NameTree<T>? nested) in _entries)
        {
            if (!first)
            {
                code.AppendLine();
            }

            first = false;
            if (nested is null)
            {
                write(code, indent, name, member);
                continue;
            }

            nested.AppendMembers(code, GeneratedClass.Open(code, indent, summary(nested._key), nested._identifier), summary, write);
            GeneratedClass.Close(code, indent);
        }
    }

    private (string Name, string? Claimant)? Refused(string name) =>
        string.Equals(name, _identifier, StringComparison.Ordinal) ? (name, null)
            : _claimedBy.TryGetValue(name, out string? claimed) ? (name, claimed)
            : null;
}
