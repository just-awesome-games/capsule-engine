using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Capsule.Generators;

// Reads which CapsuleAssets members one declaration of a scene, entity or component class reads, by symbol. A
// nested class is read on its own declaration, and the scan stops at it.
internal static class PreloadDescriber
{
    // Syntax only, on every class declaration at every keystroke. A partial part with no base list may still belong
    // to a class that derives Entity, Component or Scene, and its reads count.
    internal static bool MayRead(SyntaxNode node) =>
        node is ClassDeclarationSyntax declaration
        && (declaration.BaseList is not null || declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    // Binding every member access on every keystroke would cost the edit loop. A name filter drops every identifier
    // that cannot start a read of CapsuleAssets, and only the rest are bound. A class generated code cannot name
    // declares by hand and is passed over, as is one marked ManualPreloads.
    internal static PreloadModel? Describe(GeneratorSyntaxContext context, CancellationToken cancellation)
    {
        ClassDeclarationSyntax declaration = (ClassDeclarationSyntax)context.Node;
        SemanticModel semantic = context.SemanticModel;
        Compilation compilation = semantic.Compilation;
        if (semantic.GetDeclaredSymbol(declaration, cancellation) is not INamedTypeSymbol type
            || !(SymbolShape.DerivesFrom(type, compilation, MetadataNames.Entity)
                || SymbolShape.DerivesFrom(type, compilation, MetadataNames.Component)
                || SymbolShape.DerivesFrom(type, compilation, MetadataNames.Scene))
            || !SymbolShape.IsAccessibleFromGeneratedCode(type)
            || SymbolShape.Attribute(type, compilation, MetadataNames.ManualPreloadsAttribute) is not null)
        {
            return null;
        }

        HashSet<string> names = StartingNames(semantic, declaration.SpanStart, cancellation);
        List<string> reads = [];
        foreach (SyntaxNode node in declaration.DescendantNodes(child => child == declaration || child is not BaseTypeDeclarationSyntax))
        {
            if (node is IdentifierNameSyntax identifier
                && names.Contains(identifier.Identifier.ValueText)
                && Read(semantic, identifier, cancellation) is { } read
                && !reads.Contains(read))
            {
                reads.Add(read);
            }
        }

        return reads.Count == 0 ? null : new PreloadModel(SymbolShape.TypeOfName(type), new([.. reads]));
    }

    // Every name a read of CapsuleAssets can start with where the class is declared: CapsuleAssets itself, each alias
    // of a class under it, and each member a using static of a class under it brings into scope. The import scopes
    // hold the file's usings, those of each enclosing namespace and the project's global usings.
    private static HashSet<string> StartingNames(SemanticModel semantic, int position, CancellationToken cancellation)
    {
        HashSet<string> names = new(StringComparer.Ordinal) { "CapsuleAssets" };
        foreach (IImportScope scope in semantic.GetImportScopes(position, cancellation))
        {
            foreach (IAliasSymbol alias in scope.Aliases)
            {
                if (alias.Target is INamedTypeSymbol target && UnderAssets(target))
                {
                    names.Add(alias.Name);
                }
            }

            foreach (ImportedNamespaceOrType import in scope.Imports)
            {
                if (import.NamespaceOrType is INamedTypeSymbol imported && UnderAssets(imported))
                {
                    foreach (ISymbol member in imported.GetMembers())
                    {
                        if (member is IPropertySymbol or IFieldSymbol or INamedTypeSymbol)
                        {
                            names.Add(member.Name);
                        }
                    }
                }
            }
        }

        return names;
    }

    // The member a read starting at the identifier reaches, fully qualified, or null when it reaches no preloaded
    // member. The read climbs the access chain from the identifier while each step is a class.
    private static string? Read(SemanticModel semantic, IdentifierNameSyntax identifier, CancellationToken cancellation)
    {
        SyntaxNode current = identifier.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == identifier => access,
            QualifiedNameSyntax qualified when qualified.Right == identifier => qualified,
            _ => identifier,
        };

        if (InNameOf(semantic, current, cancellation))
        {
            return null;
        }

        ISymbol? symbol = semantic.GetSymbolInfo(current, cancellation).Symbol;
        while (symbol is INamedTypeSymbol type)
        {
            SyntaxNode? next = current.Parent switch
            {
                MemberAccessExpressionSyntax access when access.Expression == current => access,
                QualifiedNameSyntax qualified when qualified.Left == current => qualified,
                _ => null,
            };

            if (next is null)
            {
                return null;
            }

            current = next;
            symbol = semantic.GetSymbolInfo(current, cancellation).Symbol;
        }

        ITypeSymbol? read = symbol switch
        {
            IPropertySymbol { IsStatic: true } property => property.Type,
            IFieldSymbol { IsStatic: true } field => field.Type,
            _ => null,
        };

        return read is not null && Preloaded(semantic.Compilation, symbol!.ContainingType, read)
            ? SymbolShape.QualifiedName(symbol.ContainingType) + "." + CodeText.Identifier(symbol.Name)
            : null;
    }

    // Only this assembly's tree is recorded. A referenced logic assembly holds a tree of the same name, which
    // generated code could name only through an extern alias, and a class reading that one declares it by hand.
    private static bool Preloaded(Compilation compilation, INamedTypeSymbol owner, ITypeSymbol type) =>
        UnderAssets(owner)
        && SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, compilation.Assembly)
        && Array.IndexOf(MetadataNames.PreloadedAssetTypes, SymbolShape.QualifiedName(type)) >= 0;

    // Whether the class is CapsuleAssets or nested in it.
    private static bool UnderAssets(INamedTypeSymbol type)
    {
        INamedTypeSymbol outermost = type;
        while (outermost.ContainingType is { } containing)
        {
            outermost = containing;
        }

        return outermost.ToDisplayString() == MetadataNames.CapsuleAssets;
    }

    private static bool InNameOf(SemanticModel semantic, SyntaxNode node, CancellationToken cancellation)
    {
        foreach (InvocationExpressionSyntax invocation in node.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
                && semantic.GetOperation(invocation, cancellation) is INameOfOperation)
            {
                return true;
            }
        }

        return false;
    }
}
