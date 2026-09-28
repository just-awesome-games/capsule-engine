using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Refuses a project whose declared role contradicts itself or what it references.
internal static class RoleCheck
{
    internal static void Report(SourceProductionContext context, ProjectConfiguration project)
    {
        DiagnosticDescriptor? refusal = project switch
        {
            { Role: GeneratorRole.Conflict } => Diagnostics.ConflictingProjectRoles,
            { Role: GeneratorRole.Logic, ScenesReferenced: false } => Diagnostics.LogicRoleMissingScenes,
            { Role: GeneratorRole.Shell, RuntimeReferenced: false } => Diagnostics.ShellRoleMissingRuntime,
            _ => null,
        };

        if (refusal is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(refusal, Location.None));
        }
    }
}
