using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Capsule.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SaveDocumentAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Diagnostics.InitOnlySaveDocumentProperty];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(Start);
    }

    private static void Start(CompilationStartAnalysisContext context)
    {
        if (GeneratorRoles.Read(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions) == GeneratorRole.None)
        {
            return;
        }

        // A property can be reached through several save keys, and is reported once per compilation.
        ConcurrentDictionary<ISymbol, byte> reported = new(SymbolEqualityComparer.Default);
        context.RegisterOperationAction(
            operationContext => AnalyzeObjectCreation(operationContext, reported),
            OperationKind.ObjectCreation);
    }

    private static void AnalyzeObjectCreation(
        OperationAnalysisContext context,
        ConcurrentDictionary<ISymbol, byte> reported)
    {
        IObjectCreationOperation operation = (IObjectCreationOperation)context.Operation;
        if (operation.Constructor?.ContainingType is not INamedTypeSymbol created
            || created.TypeArguments.Length != 1
            || !IsSaveKey(created))
        {
            return;
        }

        Walk(created.TypeArguments[0], context, reported);
    }

    private static bool IsSaveKey(INamedTypeSymbol type)
    {
        INamedTypeSymbol definition = type.OriginalDefinition;
        return definition.MetadataName == "SaveKey`1"
            && definition.ContainingNamespace?.ToDisplayString() == "Capsule.Persistence";
    }

    // Walks the document's type graph breadth-first: T, then the type of every public instance
    // property and field it declares, through arrays and generic type arguments, stopping at a type
    // whose declaration is not in this compilation. The visited set makes a self-referencing type
    // (a linked list, a tree) terminate.
    private static void Walk(
        ITypeSymbol root,
        OperationAnalysisContext context,
        ConcurrentDictionary<ISymbol, byte> reported)
    {
        HashSet<ITypeSymbol> visited = new(SymbolEqualityComparer.Default);
        Queue<INamedTypeSymbol> queue = new();
        Enqueue(root, visited, queue);

        while (queue.Count > 0)
        {
            INamedTypeSymbol type = queue.Dequeue();
            if (!InSource(type))
            {
                continue;
            }

            foreach (ISymbol member in type.GetMembers())
            {
                switch (member)
                {
                    case IPropertySymbol { IsStatic: false, DeclaredAccessibility: Accessibility.Public } property:
                        if (IsReportable(property) && reported.TryAdd(property, 0))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                Diagnostics.InitOnlySaveDocumentProperty,
                                property.Locations.FirstOrDefault() ?? Location.None,
                                $"{type.Name}.{property.Name}"));
                        }

                        Enqueue(property.Type, visited, queue);
                        break;
                    case IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false, DeclaredAccessibility: Accessibility.Public } field:
                        Enqueue(field.Type, visited, queue);
                        break;
                }
            }
        }
    }

    private static void Enqueue(ITypeSymbol type, HashSet<ITypeSymbol> visited, Queue<INamedTypeSymbol> queue)
    {
        foreach (INamedTypeSymbol candidate in Expand(type))
        {
            if (visited.Add(candidate))
            {
                queue.Enqueue(candidate);
            }
        }
    }

    // An array's element and a generic type's own arguments (List<Item>, Dictionary<string, Item>,
    // Node?'s Nullable<Node>) are candidates too, alongside the named type itself.
    private static IEnumerable<INamedTypeSymbol> Expand(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (INamedTypeSymbol nested in Expand(array.ElementType))
                {
                    yield return nested;
                }

                break;
            case INamedTypeSymbol named:
                yield return named;
                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    foreach (INamedTypeSymbol nested in Expand(argument))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static bool InSource(INamedTypeSymbol type) =>
        type.Locations.Any(location => location.IsInSource);

    private static bool IsReportable(IPropertySymbol property) =>
        property.SetMethod is { IsInitOnly: true }
        && !property.IsRequired
        && !IsPositional(property);

    // A positional record parameter's synthesized property has no accessor syntax of its own: its
    // declaring syntax is the parameter, not a property declaration.
    private static bool IsPositional(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ParameterSyntax);
}
