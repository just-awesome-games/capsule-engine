using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Capsule.Generators;

// Each [Authorable] member is checked where it is declared, whether or not a class spawns it.
internal static class AuthorableCheck
{
    internal static AuthorableFault? Describe(GeneratorAttributeSyntaxContext marked) =>
        PropertySchema.FaultOf(marked.TargetSymbol, marked.SemanticModel.Compilation);

    // Syntax only. A positional record's parameter declares the property [property: Authorable] marks, and the
    // attribute lookup that finds a marked member never reaches a parameter.
    internal static bool MayMarkRecordProperty(SyntaxNode node) =>
        node is ParameterSyntax { Parent.Parent: RecordDeclarationSyntax } parameter
        && parameter.AttributeLists.Any(static list => list.Target?.Identifier.IsKind(SyntaxKind.PropertyKeyword) == true);

    // The fault of the property a positional record's marked parameter declares.
    internal static AuthorableFault? DescribeRecordParameter(GeneratorSyntaxContext context, CancellationToken cancellation) =>
        context.SemanticModel.GetDeclaredSymbol((ParameterSyntax)context.Node, cancellation) is { ContainingSymbol: IMethodSymbol { ContainingType: { } record } } parameter
        && record.GetMembers(parameter.Name).OfType<IPropertySymbol>().FirstOrDefault() is { } property
        && property.GetAttributes().Any(static attribute => attribute.AttributeClass?.ToDisplayString() == MetadataNames.AuthorableAttribute)
            ? PropertySchema.FaultOf(property, context.SemanticModel.Compilation)
            : null;

    internal static void Report(SourceProductionContext context, AuthorableFault fault) =>
        context.ReportDiagnostic(fault.Refusal is { } refusal
            ? Diagnostic.Create(Diagnostics.InvalidAuthorableMember, fault.At.Location(), fault.Member, refusal)
            : Diagnostic.Create(Diagnostics.DuplicateAuthorableKey, fault.At.Location(), fault.Member, fault.Key, fault.Clash));
}
