using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal static class AssetRegistrySource
{
    private const string FileName = "CapsuleGameAssets.g.cs";
    private const string DomainMetadata = "build_metadata.AdditionalFiles.CapsuleAssetDomain";
    private const string PathMetadata = "build_metadata.AdditionalFiles.CapsuleAssetPath";

    // The set member every class carries, and therefore a name no directory or file may take.
    private const string ListMember = "All";

    private const string BackingField = "_all";

    private static readonly (string Domain, string ClassName, string Handle)[] Domains =
    [
        ("textures", "Textures", "global::Capsule.Assets.TextureHandle"),
        ("audio", "Audio", "global::Capsule.Assets.AudioHandle"),
        ("fonts", "Fonts", "global::Capsule.Assets.FontHandle"),
    ];

    internal static AssetModel? Describe(AdditionalText text, AnalyzerConfigOptionsProvider options)
    {
        // Every other additional file a project carries reaches this the same way and is no asset.
        if (!options.GetOptions(text).TryGetValue(DomainMetadata, out string? domain)
            || string.IsNullOrEmpty(domain))
        {
            return null;
        }

        // MSBuild's %(RecursiveDir) carries the platform's separator; a handle has one spelling.
        string path = options.GetOptions(text).TryGetValue(PathMetadata, out string? authored) && !string.IsNullOrEmpty(authored)
            ? authored!.Replace('\\', '/')
            : Path.GetFileNameWithoutExtension(text.Path);

        return new AssetModel(domain, path, Path.GetExtension(text.Path), FaultIn(path));
    }

    internal static void Emit(SourceProductionContext context, ImmutableArray<AssetModel> models, bool emitting)
    {
        if (!emitting)
        {
            return;
        }

        List<AssetModel> sound = new(models.Length);
        foreach (AssetModel model in models)
        {
            if (model.Fault == AssetFault.UnsafeName)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RegistryDiagnostics.UnsafeAssetName, Location.None, model.Display));
                continue;
            }

            sound.Add(model);
        }

        // Sorted before the tree is built off it: the additional files arrive in whatever order
        // MSBuild collected them, and the generated source must not reorder between machines.
        sound.Sort(static (left, right) =>
        {
            int byDomain = string.CompareOrdinal(left.Domain, right.Domain);
            if (byDomain != 0)
            {
                return byDomain;
            }

            int byPath = string.CompareOrdinal(left.Path, right.Path);

            return byPath != 0 ? byPath : string.CompareOrdinal(left.Extension, right.Extension);
        });

        Node[] roots = new Node[Domains.Length];
        for (int i = 0; i < roots.Length; i++)
        {
            roots[i] = new Node(Domains[i].ClassName, Domains[i].Domain + "/");
        }

        foreach (AssetModel model in sound)
        {
            for (int i = 0; i < Domains.Length; i++)
            {
                if (string.Equals(Domains[i].Domain, model.Domain, StringComparison.Ordinal))
                {
                    Place(context, roots[i], model);
                    break;
                }
            }
        }

        context.AddSource(FileName, SourceText.From(Render(roots), Encoding.UTF8));
    }

    // Every segment of the path is an identifier in its own right.
    private static AssetFault FaultIn(string path)
    {
        foreach (string segment in path.Split('/'))
        {
            if (TypeNaming.ToIdentifier(segment) is null)
            {
                return AssetFault.UnsafeName;
            }
        }

        return AssetFault.None;
    }

    // Walks the model's directories into the tree and hangs the handle off the last one, reporting
    // every collision the rendered C# could not compile against both parties.
    private static void Place(SourceProductionContext context, Node root, AssetModel model)
    {
        string[] segments = model.Path.Split('/');
        List<Node> walked = new(segments.Length) { root };
        Node node = root;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            string identifier = TypeNaming.ToIdentifier(segments[i])!;
            string display = node.Display + segments[i] + "/";

            if (!Claimable(context, node, identifier, display))
            {
                return;
            }

            if (!node.Directories.TryGetValue(identifier, out Node? child))
            {
                child = new Node(identifier, display);
                node.Directories.Add(identifier, child);
                node.ClaimedBy.Add(identifier, display);
            }

            node = child;
            walked.Add(child);
        }

        string leaf = TypeNaming.ToIdentifier(segments[segments.Length - 1])!;
        if (!Claimable(context, node, leaf, model.Display))
        {
            return;
        }

        node.Leaves.Add(leaf, model);
        node.ClaimedBy.Add(leaf, model.Display);

        // A directory is a set, and its subdirectories are in it.
        foreach (Node held in walked)
        {
            held.All.Add(model);
        }
    }

    // Whether an identifier may be declared on this class: not the class's own name (CS0542), not
    // the set member every class carries, and not one already claimed here.
    private static bool Claimable(SourceProductionContext context, Node node, string identifier, string display)
    {
        if (string.Equals(identifier, node.Identifier, StringComparison.Ordinal)
            || string.Equals(identifier, ListMember, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RegistryDiagnostics.AssetNamedAfterItsDomain, Location.None, display, identifier, node.Display));

            return false;
        }

        if (node.ClaimedBy.TryGetValue(identifier, out string? claimed))
        {
            // A directory declared twice is one class, not a collision.
            if (node.Directories.ContainsKey(identifier) && string.Equals(claimed, display, StringComparison.Ordinal))
            {
                return true;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                RegistryDiagnostics.DuplicateAssetIdentifier, Location.None, claimed, display, identifier, node.Display));

            return false;
        }

        return true;
    }

    private static string Render(Node[] roots)
    {
        StringBuilder source = new();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Capsule.Assets.Generated");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>Every asset this game ships, as typed handles. Generated; do not edit.</summary>");
        source.AppendLine("    [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
        source.AppendLine("    public static class GameAssets");
        source.AppendLine("    {");

        // Every domain is emitted whatever the game authored, so a call site naming one compiles.
        for (int i = 0; i < roots.Length; i++)
        {
            if (i > 0)
            {
                source.AppendLine();
            }

            AppendClass(source, roots[i], Domains[i].Handle, "        ");
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    private static void AppendClass(StringBuilder source, Node node, string handle, string indent)
    {
        string shipped = "assets/" + node.Display.Substring(0, node.Display.Length - 1);

        source.Append(indent).Append("/// <summary>Everything shipped at <c>").Append(shipped).AppendLine("</c>.</summary>");
        source.Append(indent).Append("public static class ").AppendLine(node.Identifier);
        source.Append(indent).AppendLine("{");

        string inner = indent + "    ";
        bool first = true;

        foreach (KeyValuePair<string, AssetModel> leaf in node.Leaves)
        {
            if (!first)
            {
                source.AppendLine();
            }

            first = false;

            source.Append(inner).Append("/// <summary><c>").Append(leaf.Value.Display).AppendLine("</c>.</summary>");
            source.Append(inner).Append("public static ").Append(handle).Append(' ').Append(leaf.Key);
            source.Append(" => new ").Append(handle).Append('(');
            source.Append(SymbolDisplay.FormatLiteral(leaf.Value.Path, quote: true));
            source.Append(", ");
            source.Append(SymbolDisplay.FormatLiteral(leaf.Value.Extension, quote: true));
            source.AppendLine(");");
        }

        foreach (KeyValuePair<string, Node> directory in node.Directories)
        {
            if (!first)
            {
                source.AppendLine();
            }

            first = false;

            AppendClass(source, directory.Value, handle, inner);
        }

        AppendList(source, node, handle, shipped, inner, first);

        source.Append(indent).AppendLine("}");
    }

    // Backed by a field and handed out as a span: allocation-free to enumerate, and read-only.
    private static void AppendList(StringBuilder source, Node node, string handle, string shipped, string indent, bool first)
    {
        if (!first)
        {
            source.AppendLine();
        }

        source.Append(indent).Append("private static readonly ").Append(handle).Append("[] ").Append(BackingField);
        source.AppendLine(" =");
        source.Append(indent).Append("    new ").Append(handle).AppendLine("[]");
        source.Append(indent).AppendLine("    {");

        foreach (AssetModel model in node.All)
        {
            source.Append(indent).Append("        ").Append(Reference(node, model)).AppendLine(",");
        }

        source.Append(indent).AppendLine("    };");
        source.AppendLine();
        source.Append(indent).Append("/// <summary>Every asset shipped under <c>").Append(shipped)
            .AppendLine("</c>, its subdirectories included.</summary>");
        source.Append(indent).Append("public static global::System.ReadOnlySpan<").Append(handle).Append("> ")
            .Append(ListMember).Append(" => ").Append(BackingField).AppendLine(";");
    }

    // How the handle is named from inside this class.
    private static string Reference(Node node, AssetModel model)
    {
        string relative = model.Path.Substring(node.Depth);
        StringBuilder reference = new();

        foreach (string segment in relative.Split('/'))
        {
            if (reference.Length > 0)
            {
                reference.Append('.');
            }

            reference.Append(TypeNaming.ToIdentifier(segment)!);
        }

        return reference.ToString();
    }

    private sealed class Node
    {
        internal Node(string identifier, string display)
        {
            Identifier = identifier;
            Display = display;

            // The domain root's display is '<domain>/', which is no part of a model's path.
            Depth = display.Length - display.IndexOf('/') - 1;
        }

        internal string Identifier { get; }

        /// <summary>The source directory this class stands for, ending in a separator.</summary>
        internal string Display { get; }

        /// <summary>How much of a model's path this class has already spelled.</summary>
        internal int Depth { get; }

        internal SortedDictionary<string, AssetModel> Leaves { get; } = new(StringComparer.Ordinal);

        internal SortedDictionary<string, Node> Directories { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> ClaimedBy { get; } = new(StringComparer.Ordinal);

        /// <summary>Every asset beneath this class, transitively, in ordinal path order.</summary>
        internal List<AssetModel> All { get; } = [];
    }
}
