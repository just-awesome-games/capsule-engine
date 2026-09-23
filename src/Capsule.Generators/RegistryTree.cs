using System.Text;

namespace Capsule.Generators;

/// <summary>Writes one member of a registry class at <paramref name="indent"/>, backing field included.</summary>
internal delegate void RegistryLeafWriter<T>(StringBuilder source, string indent, string identifier, T value);

/// <summary>Whether <paramref name="identifier"/> may be declared on <paramref name="node"/>.</summary>
/// <param name="leaf">Whether it names the source itself, not a directory above it.</param>
internal delegate bool RegistryClaimCheck<T>(RegistryNode<T> node, string identifier, string display, bool leaf);

/// <summary>Why an identifier cannot be declared where a key puts it.</summary>
internal enum RegistryFault
{
    /// <summary>It is the name of the generated class it would be declared on (CS0542).</summary>
    NamedAfterItsClass,

    /// <summary>It is a name the generated classes take for a member of their own.</summary>
    NamedAfterAGeneratedMember,

    /// <summary>Something else in that directory already declares it.</summary>
    AlreadyDeclared,
}

/// <summary>One refused identifier, in the terms the refusing domain reports it.</summary>
/// <param name="Display">The name a diagnostic gives the refused source.</param>
/// <param name="Directory">The source directory whose generated class refused it.</param>
/// <param name="ClaimedBy">What already declares it, for <see cref="RegistryFault.AlreadyDeclared"/>.</param>
internal readonly record struct RegistryRefusal(
    RegistryFault Fault,
    string Identifier,
    string Display,
    string Directory,
    string? ClaimedBy);

/// <summary>The rule every generated registry admits an identifier by.</summary>
internal static class RegistryClaims
{
    /// <param name="refuse">Reports the defect in the calling domain's own terms.</param>
    /// <param name="reserves">Which identifiers the domain's generated members take. Null reserves just the set member.</param>
    internal static RegistryClaimCheck<T> Check<T>(
        Action<RegistryRefusal> refuse,
        Func<string, bool, bool>? reserves = null) =>
        (node, identifier, display, leaf) =>
        {
            if (string.Equals(identifier, node.Identifier, StringComparison.Ordinal))
            {
                refuse(new RegistryRefusal(RegistryFault.NamedAfterItsClass, identifier, display, node.Display, null));

                return false;
            }

            bool reserved = reserves is null
                ? string.Equals(identifier, RegistryFile.ListMember, StringComparison.Ordinal)
                : reserves(identifier, leaf);

            if (reserved)
            {
                refuse(new RegistryRefusal(RegistryFault.NamedAfterAGeneratedMember, identifier, display, node.Display, null));

                return false;
            }

            if (node.ClaimedBy.TryGetValue(identifier, out string? claimed))
            {
                // A directory two sources share is one class, not a collision.
                if (node.Directories.ContainsKey(identifier) && string.Equals(claimed, display, StringComparison.Ordinal))
                {
                    return true;
                }

                refuse(new RegistryRefusal(RegistryFault.AlreadyDeclared, identifier, display, node.Display, claimed));

                return false;
            }

            return true;
        };
}

// One directory of a generated registry: the members declared on it, the nested classes under it,
// and every member beneath it transitively.
internal sealed class RegistryNode<T>
{
    internal RegistryNode(string identifier, string display)
    {
        Identifier = identifier;
        Display = display;

        // The domain root's display is '<domain>/', which no key includes.
        Depth = display.Length - display.IndexOf('/') - 1;
    }

    internal string Identifier { get; }

    /// <summary>The source directory this class stands for, domain root included, ending in a separator.</summary>
    internal string Display { get; }

    /// <summary>How much of a key this class has already spelled.</summary>
    internal int Depth { get; }

    internal SortedDictionary<string, T> Leaves { get; } = new(StringComparer.Ordinal);

    internal SortedDictionary<string, RegistryNode<T>> Directories { get; } = new(StringComparer.Ordinal);

    /// <summary>What already declares each identifier here, keyed by identifier and holding its display name.</summary>
    internal Dictionary<string, string> ClaimedBy { get; } = new(StringComparer.Ordinal);

    /// <summary>Every key beneath this class, transitively, in ordinal order.</summary>
    internal List<string> All { get; } = [];
}

// The shape every generated asset registry has: a domain class of nested static classes, one per
// authored directory, each carrying the members keyed into it and an 'All' set of everything
// beneath it. A null memberType declares no set.
internal sealed class RegistryDomain<T>(
    string registryClass,
    string domain,
    string? memberType,
    string noun,
    string summary,
    RegistryLeafWriter<T> leaf)
{
    private const string BackingField = "_all";

    internal RegistryNode<T> Root { get; } = new(registryClass, domain + "/");

    /// <summary>
    /// Hangs <paramref name="value"/> off the class <paramref name="key"/> names, creating the
    /// directory classes it walks through. Returns false when <paramref name="claim"/> refused a
    /// name on the way.
    /// </summary>
    /// <param name="key">The source's key under the domain root, forward slashes and no extension.</param>
    /// <param name="display">The name a diagnostic gives the source.</param>
    /// <param name="value">What the leaf member hands back.</param>
    /// <param name="claim">Checks each identifier before it is declared.</param>
    internal bool Add(string key, string display, T value, RegistryClaimCheck<T> claim)
    {
        string[] segments = key.Split('/');
        List<RegistryNode<T>> walked = new(segments.Length) { Root };
        RegistryNode<T> node = Root;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            string identifier = TypeNaming.ToIdentifier(segments[i])!;
            string directory = node.Display + segments[i] + "/";

            if (!claim(node, identifier, directory, leaf: false))
            {
                return false;
            }

            if (!node.Directories.TryGetValue(identifier, out RegistryNode<T>? child))
            {
                child = new RegistryNode<T>(identifier, directory);
                node.Directories.Add(identifier, child);
                node.ClaimedBy.Add(identifier, directory);
            }

            node = child;
            walked.Add(child);
        }

        string name = TypeNaming.ToIdentifier(segments[segments.Length - 1])!;
        if (!claim(node, name, display, leaf: true))
        {
            return false;
        }

        node.Leaves.Add(name, value);
        node.ClaimedBy.Add(name, display);

        // A directory is a set, and its subdirectories are in it.
        foreach (RegistryNode<T> held in walked)
        {
            held.All.Add(key);
        }

        return true;
    }

    internal void Append(StringBuilder source, string indent) =>
        Append(source, Root, indent, summary, attributed: true);

    private void Append(StringBuilder source, RegistryNode<T> node, string indent, string classSummary, bool attributed)
    {
        string shipped = "assets/" + node.Display.Substring(0, node.Display.Length - 1);

        source.Append(indent).Append("/// <summary>").Append(classSummary).AppendLine("</summary>");
        if (attributed)
        {
            source.Append(indent).AppendLine("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        }

        source.Append(indent).Append("public static class ").AppendLine(node.Identifier);
        source.Append(indent).AppendLine("{");

        string inner = indent + "    ";
        bool first = true;

        foreach (KeyValuePair<string, T> declared in node.Leaves)
        {
            if (!first)
            {
                source.AppendLine();
            }

            first = false;

            leaf(source, inner, declared.Key, declared.Value);
        }

        foreach (KeyValuePair<string, RegistryNode<T>> directory in node.Directories)
        {
            if (!first)
            {
                source.AppendLine();
            }

            first = false;

            string nested = directory.Value.Display.Substring(0, directory.Value.Display.Length - 1);
            Append(
                source,
                directory.Value,
                inner,
                memberType is null
                    ? "The " + noun + "s authored under <c>" + nested + "</c>."
                    : "Everything shipped at <c>assets/" + nested + "</c>.",
                attributed: false);
        }

        if (memberType is not null)
        {
            AppendList(source, node, shipped, inner, first);
        }

        source.Append(indent).AppendLine("}");
    }

    // Handed out as a span over a backing field, so enumerating it allocates nothing.
    private void AppendList(StringBuilder source, RegistryNode<T> node, string shipped, string indent, bool first)
    {
        if (!first)
        {
            source.AppendLine();
        }

        source.Append(indent).Append("private static readonly ").Append(memberType).Append("[] ").Append(BackingField);
        source.AppendLine(" =");
        source.Append(indent).Append("    new ").Append(memberType).AppendLine("[]");
        source.Append(indent).AppendLine("    {");

        foreach (string key in node.All)
        {
            source.Append(indent).Append("        ").Append(Reference(node, key)).AppendLine(",");
        }

        source.Append(indent).AppendLine("    };");
        source.AppendLine();
        source.Append(indent).Append("/// <summary>Every ").Append(noun).Append(" shipped under <c>").Append(shipped)
            .AppendLine("</c>, its subdirectories included.</summary>");
        source.Append(indent).Append("public static global::System.ReadOnlySpan<").Append(memberType).Append("> ")
            .Append(RegistryFile.ListMember).Append(" => ").Append(BackingField).AppendLine(";");
    }

    private static string Reference(RegistryNode<T> node, string key)
    {
        StringBuilder reference = new();

        foreach (string segment in key.Substring(node.Depth).Split('/'))
        {
            if (reference.Length > 0)
            {
                reference.Append('.');
            }

            reference.Append(TypeNaming.ToIdentifier(segment)!);
        }

        return reference.ToString();
    }
}

// The generated class every asset domain declares itself inside.
internal static class RegistryFile
{
    internal const string RootClass = "CapsuleAssets";

    /// <summary>The set member every class carries. No directory or file may take this name.</summary>
    internal const string ListMember = "All";

    // Every half of this partial class carries the same summary, so the compiler keeps the same
    // text whichever half it picks. Each half attributes only the classes it declares, because a
    // non-repeatable attribute named by two halves of one partial class is CS0579.
    internal static StringBuilder Open()
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Capsule.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Every asset this game ships and every sprite sheet it authors. Generated code. Do not edit.</summary>");
        source.Append("    public static partial class ").AppendLine(RootClass);
        source.AppendLine("    {");

        return source;
    }

    internal static string Close(StringBuilder source)
    {
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }
}
