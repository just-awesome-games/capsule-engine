using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Refuses a shipped document that names a baseScene when a class already claims the document. The
// document would then name two bases for one scene, so this is not a question of which one wins.
internal static class DocumentClaimCheck
{
    internal static void Run(SourceProductionContext context, DocumentClaimInputs inputs)
    {
        Dictionary<string, SceneDocumentModel> documents = new(StringComparer.Ordinal);
        foreach (SceneDocumentModel document in inputs.Documents.Items)
        {
            documents[document.Key] = document;
        }

        foreach (RegisteredScene entry in inputs.Scenes.Registered.Items)
        {
            if (entry.DocumentName is { } name
                && documents.TryGetValue(name, out SceneDocumentModel claimed)
                && claimed.BaseScene is { } conflicting)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DocumentBaseSceneConflictsWithAClaim,
                    entry.Model.At.Location(),
                    name,
                    conflicting,
                    entry.Model.DisplayName));
            }
        }
    }
}
