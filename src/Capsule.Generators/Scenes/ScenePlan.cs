using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>One scene class registered, with the document it claims, if any.</summary>
/// <param name="DocumentName">The document the class composes, or null for a scene with no document.</param>
/// <param name="Camera">The fully qualified camera the document names, or null.</param>
internal readonly record struct RegisteredScene(string? DocumentName, SceneModel Model, string? Camera);

/// <summary>One shipped document no class claims, composed by the engine's Scene or by a generated subclass of its baseScene.</summary>
/// <param name="Camera">The fully qualified camera the document names, or null.</param>
internal readonly record struct DocumentOnlyScene(string DocumentName, GeneratedBase? Base, string? Camera);

/// <summary>The internal sealed scene a document's baseScene generates.</summary>
/// <param name="Base">The abstract scene it derives.</param>
internal readonly record struct GeneratedBase(string ClassName, SceneModel Base);

/// <summary>What <c>CapsuleScenes.g.cs</c> holds, resolved from every scene class, camera class and shipped document.</summary>
/// <param name="Generates">Whether the assembly gets the file at all: only a logic assembly does.</param>
/// <param name="Registered">Every sound scene class, one per document, those with no document first.</param>
/// <param name="DocumentOnly">Every document no class claims, sorted by key.</param>
/// <param name="Lookups">The asset lookups the composing classes' authored members read through.</param>
/// <param name="Diagnostics">Every fault found resolving the plan.</param>
internal readonly record struct ScenePlan(
    bool Generates,
    EquatableArray<RegisteredScene> Registered,
    EquatableArray<DocumentOnlyScene> DocumentOnly,
    EquatableArray<AssetLookup> Lookups,
    EquatableArray<Diagnostic> Diagnostics)
{
    /// <summary>Every class whose members a document's properties set: a registered class or a generated scene's base.</summary>
    internal IEnumerable<SceneModel> Composing => Registered.Items
        .Where(static entry => entry.DocumentName is not null)
        .Select(static entry => entry.Model)
        .Concat(DocumentOnly.Items.Where(static document => document.Base is not null).Select(static document => document.Base!.Value.Base))
        .Where(static model => model.Authored.Any())
        .GroupBy(static model => model.QualifiedName)
        .Select(static models => models.First());
}
