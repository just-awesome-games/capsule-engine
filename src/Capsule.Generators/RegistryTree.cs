using System.Text;

namespace Capsule.Generators;

/// <summary>Writes one member of a registry class at <paramref name="indent"/>, backing included.</summary>
internal delegate void RegistryLeafWriter<T>(StringBuilder source, string indent, string identifier, T value);

/// <summary>Whether <paramref name="identifier"/> may be declared on <paramref name="node"/>.</summary>
internal delegate bool RegistryClaimCheck<T>(RegistryNode<T> node, string identifier, string display);

// One directory of a generated registry: the members declared on it, the nested classes under it,
// and every member beneath it transitively.
internal sealed class RegistryNode<T>
{
    internal RegistryNode(string identifier, string display)
    {
        Identifier = identifier;
        Display = display;

        // The domain root's display is '<domain>/', which is no part of a key.
        Depth = display.Length - display.IndexOf('/') - 1;
    }

    internal string Identifier { get; }

    /// <summary>The source directory this class stands for, domain root included, ending in a separator.</summary>
    internal string Display { get; }

    /// <summary>How much of a key this class has already spelled.</summary>
    internal int Depth { get; }

    internal SortedDictionary<string, T> Leaves { get; } = new(StringComparer.Ordinal);

    internal SortedDictionary<string, RegistryNode<T>> Directories { get; } = new(StringComparer.Ordinal);

    /// <summary>What already declares each identifier here, by the display name a diagnostic knows it as.</summary>
    internal Dictionary<string, string> ClaimedBy { get; } = new(StringComparer.Ordinal);

    /// <summary>Every key beneath this class, transitively, in ordinal order.</summary>
    internal List<string> All { get; } = [];
}

// The shape every generated asset registry has: a domain class of nested static classes, one per
// authored directory, each carrying the members keyed into it and an 'All' set of everything
// beneath it. A domain writes only its own leaves.
internal sealed class RegistryDomain<T>(
    string registryClass,
    string domain,
    string memberType,
    string noun,
    string summary,
    RegistryLeafWriter<T> leaf)
{
    private const string BackingField = "_all";

    internal RegistryNode<T> Root { get; } = new(registryClass, domain + "/");

    /// <summary>
    /// Hangs <paramref name="value"/> off the class <paramref name="key"/> names, creating the
    /// directories it walks through. False when <paramref name="claim"/> refused a name on the way.
    /// </summary>
    /// <param name="key">The source's key under the domain root, forward slashes and no extension.</param>
    /// <param name="display">What a diagnostic names the source by.</param>
    /// <param name="value">What the leaf member hands back.</param>
    /// <param name="claim">Checks each identifier before it is declared; null declares them all.</param>
    internal bool Add(string key, string display, T value, RegistryClaimCheck<T>? claim = null)
    {
        string[] segments = key.Split('/');
        List<RegistryNode<T>> walked = new(segments.Length) { Root };
        RegistryNode<T> node = Root;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            string identifier = TypeNaming.ToIdentifier(segments[i])!;
            string directory = node.Display + segments[i] + "/";

            if (claim is not null && !claim(node, identifier, directory))
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
        if (claim is not null && !claim(node, name, display))
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

            string nested = directory.Value.Display;
            Append(
                source,
                directory.Value,
                inner,
                "Everything shipped at <c>assets/" + nested.Substring(0, nested.Length - 1) + "</c>.",
                attributed: false);
        }

        AppendList(source, node, shipped, inner, first);

        source.Append(indent).AppendLine("}");
    }

    // Backed by a field and handed out as a span: allocation-free to enumerate, and read-only.
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

    // How the member is named from inside this class.
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

// The one generated class every asset domain declares itself inside.
internal static class RegistryFile
{
    internal const string RootClass = "CapsuleAssets";

    /// <summary>The set member every class carries, and therefore a name no directory or file may take.</summary>
    internal const string ListMember = "All";

    // Every half of this partial class carries the same summary, so whichever the compiler keeps is
    // the same text, and each half attributes only the classes it declares: a non-repeatable
    // attribute named by two halves of one partial class is CS0579.
    internal static StringBuilder Open()
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Capsule.Assets.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Every asset this game ships and every sprite sheet it authors. Generated; do not edit.</summary>");
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
