using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Generators;

// The generated applier writes an [Authorable] field through an [UnsafeAccessor] no analysis sees. Two warnings
// about such a field are then wrong, and each needs its own mechanism. This suppressor quiets the compiler's
// never-assigned warning, which no attribute reaches. AuthorableSuppressionRenderer writes the attributes that
// suppress the advice to make the field readonly. dotnet format applies that advice and ignores a suppressor.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuthorableSuppressor : DiagnosticSuppressor
{
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [Diagnostics.UnassignedAuthorableField];

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        INamedTypeSymbol? authorable = context.Compilation.GetTypeByMetadataName(MetadataNames.AuthorableAttribute);
        if (authorable is null)
        {
            return;
        }

        foreach (Diagnostic diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Location.SourceTree is not { } tree)
            {
                continue;
            }

            VariableDeclaratorSyntax? declarator = tree.GetRoot(context.CancellationToken)
                .FindNode(diagnostic.Location.SourceSpan)
                .AncestorsAndSelf()
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            if (declarator is not null
                && context.GetSemanticModel(tree).GetDeclaredSymbol(declarator, context.CancellationToken) is IFieldSymbol field
                && field.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, authorable)))
            {
                context.ReportSuppression(Suppression.Create(Diagnostics.UnassignedAuthorableField, diagnostic));
            }
        }
    }
}
