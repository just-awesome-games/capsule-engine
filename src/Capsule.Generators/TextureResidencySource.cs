using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

/// <summary>
/// Which texture groups each declared type reaches, closed over the types it references. A group is
/// one generated directory class's set — <c>GameAssets.Textures.Enemies.All</c> — and a scene's
/// residency set is the union of the groups its class and its document's spawn types reach.
/// </summary>
internal sealed class TextureResidency
{
    internal static readonly TextureResidency None = new(new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal));

    /// <summary>The generated tree a game names its textures through.</summary>
    private const string AssetsClass = "GameAssets";

    private const string TexturesClass = "Textures";

    private const string AssetsReference = "global::Capsule.Assets.Generated.GameAssets.Textures";

    /// <summary>The set member every generated asset class carries.</summary>
    private const string ListMember = "All";

    private const string TextureDomain = "textures";

    private readonly Dictionary<string, ImmutableArray<string>> _groups;

    private TextureResidency(Dictionary<string, ImmutableArray<string>> groups) => _groups = groups;

    /// <summary>
    /// The groups <paramref name="qualifiedName"/> reaches, ordinal-sorted, or empty when it
    /// reaches none.
    /// </summary>
    internal ImmutableArray<string> GroupsOf(string qualifiedName) =>
        _groups.TryGetValue(qualifiedName, out ImmutableArray<string> groups) ? groups : ImmutableArray<string>.Empty;

    /// <summary>How generated code names one group's handles.</summary>
    internal static string ReferenceTo(string group) =>
        group.Length == 0 ? AssetsReference + "." + ListMember : AssetsReference + "." + group + "." + ListMember;

    /// <summary>
    /// Walks every type this compilation declares once, then closes each type's own groups over the
    /// types it references.
    /// </summary>
    /// <param name="compilation">The game's logic assembly.</param>
    /// <param name="assets">Everything the asset hook handed the compiler, all domains.</param>
    internal static TextureResidency Derive(
        Compilation compilation,
        ImmutableArray<AssetModel> assets,
        CancellationToken cancellation)
    {
        HashSet<string> directories = TextureDirectories(assets);
        Dictionary<string, Walked> walked = new(StringComparer.Ordinal);

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            cancellation.ThrowIfCancellationRequested();

            SemanticModel? model = null;
            foreach (TypeDeclarationSyntax declaration in tree.GetRoot(cancellation).DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                model ??= compilation.GetSemanticModel(tree);
                if (model.GetDeclaredSymbol(declaration, cancellation) is not INamedTypeSymbol type)
                {
                    continue;
                }

                string qualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!walked.TryGetValue(qualifiedName, out Walked entry))
                {
                    // The parts of a partial class are one type, and each part contributes.
                    entry = new Walked();
                    walked.Add(qualifiedName, entry);
                }

                WalkDeclaration(compilation, model, declaration, directories, entry, cancellation);
            }
        }

        Dictionary<string, ImmutableArray<string>> closed = new(walked.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, Walked> entry in walked)
        {
            cancellation.ThrowIfCancellationRequested();
            closed.Add(entry.Key, Close(entry.Key, walked));
        }

        return new TextureResidency(closed);
    }

    /// <summary>Every directory the texture domain declares, as the dotted identifier path its class has.</summary>
    private static HashSet<string> TextureDirectories(ImmutableArray<AssetModel> assets)
    {
        // The domain root is a directory in its own right, and the group of anything filed loose.
        HashSet<string> directories = new(StringComparer.Ordinal) { string.Empty };

        foreach (AssetModel asset in assets)
        {
            if (asset.Fault != AssetFault.None || !string.Equals(asset.Domain, TextureDomain, StringComparison.Ordinal))
            {
                continue;
            }

            string[] segments = asset.Path.Split('/');
            string path = string.Empty;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string? identifier = TypeNaming.ToIdentifier(segments[i]);
                if (identifier is null)
                {
                    break;
                }

                path = path.Length == 0 ? identifier : path + "." + identifier;
                directories.Add(path);
            }
        }

        return directories;
    }

    private static void WalkDeclaration(
        Compilation compilation,
        SemanticModel model,
        TypeDeclarationSyntax declaration,
        HashSet<string> directories,
        Walked entry,
        CancellationToken cancellation)
    {
        // A nested type is walked as itself, and reached from here as a reference like any other.
        IEnumerable<SyntaxNode> nodes = declaration.DescendantNodes(
            node => ReferenceEquals(node, declaration) || node is not TypeDeclarationSyntax);

        foreach (SyntaxNode node in nodes)
        {
            cancellation.ThrowIfCancellationRequested();

            switch (node)
            {
                case MemberAccessExpressionSyntax access when !IsInnerLink(access):
                    if (AssetGroup(access, directories) is { } group)
                    {
                        entry.Groups.Add(group);
                    }

                    break;

                case ObjectCreationExpressionSyntax creation:
                    if (LiteralHandleGroup(model, creation, directories) is { } literal)
                    {
                        entry.Groups.Add(literal);
                    }

                    break;

                case SimpleNameSyntax name:
                    if (Referenced(compilation, model, name, cancellation) is { } referenced)
                    {
                        entry.References.Add(referenced);
                    }

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Whether this access is the left-hand side of a longer one, which carries the whole chain.</summary>
    private static bool IsInnerLink(MemberAccessExpressionSyntax access) =>
        access.Parent is MemberAccessExpressionSyntax parent && ReferenceEquals(parent.Expression, access);

    /// <summary>
    /// The group a <c>GameAssets.Textures....</c> chain names, or null when the chain is not one.
    /// Matched on syntax: the asset registry is a sibling generator's output, so its members do not
    /// bind while this one runs.
    /// </summary>
    private static string? AssetGroup(MemberAccessExpressionSyntax access, HashSet<string> directories)
    {
        List<string> chain = [];
        ExpressionSyntax current = access;
        while (current is MemberAccessExpressionSyntax link)
        {
            chain.Add(link.Name.Identifier.ValueText);
            current = link.Expression;
        }

        if (current is not IdentifierNameSyntax root)
        {
            return null;
        }

        chain.Add(root.Identifier.ValueText);
        chain.Reverse();

        int domain = chain.IndexOf(AssetsClass);
        if (domain < 0 || domain + 1 >= chain.Count || !string.Equals(chain[domain + 1], TexturesClass, StringComparison.Ordinal))
        {
            return null;
        }

        // The deepest directory the chain spells: what follows is a handle, its members, or All.
        string group = string.Empty;
        for (int i = domain + 2; i < chain.Count; i++)
        {
            string deeper = group.Length == 0 ? chain[i] : group + "." + chain[i];
            if (!directories.Contains(deeper))
            {
                break;
            }

            group = deeper;
        }

        return group;
    }

    /// <summary>
    /// The group a <c>new TextureHandle("actors/player", ".png")</c> names. Sprite sheets and other
    /// derived registries construct handles rather than naming the asset registry's members.
    /// </summary>
    private static string? LiteralHandleGroup(
        SemanticModel model,
        ObjectCreationExpressionSyntax creation,
        HashSet<string> directories)
    {
        if (creation.ArgumentList is not { Arguments.Count: 2 } arguments
            || arguments.Arguments[0].Expression is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression)
            || model.GetSymbolInfo(creation).Symbol is not IMethodSymbol constructor
            || !string.Equals(constructor.ContainingType?.ToDisplayString(), Symbols.TextureHandle, StringComparison.Ordinal))
        {
            return null;
        }

        string name = literal.Token.ValueText;
        int separator = name.LastIndexOf('/');

        return separator < 0 ? string.Empty : DirectoryOf(name.Substring(0, separator), directories);
    }

    /// <summary>The class path a handle's directory has, cut back to the deepest one that exists.</summary>
    private static string DirectoryOf(string path, HashSet<string> directories)
    {
        string group = string.Empty;

        foreach (string segment in path.Split('/'))
        {
            string? identifier = TypeNaming.ToIdentifier(segment);
            if (identifier is null)
            {
                break;
            }

            string deeper = group.Length == 0 ? identifier : group + "." + identifier;
            if (!directories.Contains(deeper))
            {
                break;
            }

            group = deeper;
        }

        return group;
    }

    /// <summary>The type this name reaches, when it is one this compilation declares.</summary>
    private static string? Referenced(
        Compilation compilation,
        SemanticModel model,
        SimpleNameSyntax name,
        CancellationToken cancellation)
    {
        SymbolInfo info = model.GetSymbolInfo(name, cancellation);
        ISymbol? symbol = info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);

        INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? symbol?.ContainingType;

        // Types from metadata carry no syntax to walk, and the engine's own are not a game's assets.
        if (type is null || type.DeclaringSyntaxReferences.Length == 0)
        {
            return null;
        }

        // A scene naming another scene is a transition, and residency is per scene: following the
        // edge would make every scene reachable from the boot scene resident at boot.
        return Symbols.DerivesFrom(type, compilation, Symbols.Scene)
            ? null
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static ImmutableArray<string> Close(string root, Dictionary<string, Walked> walked)
    {
        SortedSet<string> groups = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal) { root };
        Stack<string> pending = new();
        pending.Push(root);

        while (pending.Count > 0)
        {
            if (!walked.TryGetValue(pending.Pop(), out Walked entry))
            {
                continue;
            }

            foreach (string group in entry.Groups)
            {
                groups.Add(group);
            }

            foreach (string reference in entry.References)
            {
                if (visited.Add(reference))
                {
                    pending.Push(reference);
                }
            }
        }

        return groups.Count == 0 ? ImmutableArray<string>.Empty : [.. groups];
    }

    private sealed class Walked
    {
        internal HashSet<string> Groups { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> References { get; } = new(StringComparer.Ordinal);
    }
}
