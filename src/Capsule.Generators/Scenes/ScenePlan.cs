using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>One scene class registered, with the document it claims, if any.</summary>
/// <param name="DocumentName">The document the class composes, or null for a scene with no document.</param>
internal readonly record struct RegisteredScene(string? DocumentName, SceneModel Model);

/// <summary>One shipped document no class claims, composed by the engine's Scene or by a generated subclass of its baseScene.</summary>
internal readonly record struct DocumentOnlyScene(string DocumentName, GeneratedBase? Base);

/// <summary>The internal sealed scene a document's baseScene generates.</summary>
/// <param name="Base">The abstract scene it derives.</param>
internal readonly record struct GeneratedBase(string ClassName, SceneModel Base);

/// <summary>What <c>CapsuleScenes.g.cs</c> holds, resolved from every scene class and every shipped document.</summary>
/// <param name="Generates">Whether the assembly gets the file at all: only a logic assembly does.</param>
/// <param name="Registered">Every sound scene class, one per document, those with no document first.</param>
/// <param name="DocumentOnly">Every document no class claims, sorted by key.</param>
/// <param name="Applied">
/// Every scene class an applier is emitted for, sorted by name: each class generated code can name, the engine's
/// plain Scene among them, plus each class a document composes.
/// </param>
/// <param name="Objects">Every class the applied scenes' members read a JSON object into, at any depth.</param>
/// <param name="Lookups">The asset lookups the composing classes' authored members read through.</param>
/// <param name="Diagnostics">Every fault found resolving the plan.</param>
internal readonly record struct ScenePlan(
    bool Generates,
    EquatableArray<RegisteredScene> Registered,
    EquatableArray<DocumentOnlyScene> DocumentOnly,
    EquatableArray<SceneModel> Applied,
    EquatableArray<KeyedObject> Objects,
    EquatableArray<AssetLookup> Lookups,
    EquatableArray<Diagnostic> Diagnostics);
