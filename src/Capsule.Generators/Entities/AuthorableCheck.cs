using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Each [Authorable] member is checked where it is declared, whether or not a class spawns it.
internal static class AuthorableCheck
{
    internal static AuthorableFault? Describe(GeneratorAttributeSyntaxContext marked) =>
        PropertySchema.FaultOf(marked.TargetSymbol, marked.SemanticModel.Compilation);

    internal static void Report(SourceProductionContext context, AuthorableFault fault) =>
        context.ReportDiagnostic(fault.Refusal is { } refusal
            ? Diagnostic.Create(Diagnostics.InvalidAuthorableMember, fault.At.Location(), fault.Member, refusal)
            : Diagnostic.Create(Diagnostics.DuplicateAuthorableKey, fault.At.Location(), fault.Member, fault.Key, fault.Clash));
}
