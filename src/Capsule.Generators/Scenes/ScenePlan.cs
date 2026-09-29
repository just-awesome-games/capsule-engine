using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>One scene class registered, with the document it claims, if any.</summary>
/// <param name="DocumentName">The document the class composes, or null for a scene with no document.</param>
/// <param name="Camera">The fully qualified camera the document names, or null.</param>
internal readonly record struct RegisteredScene(string? DocumentName, SceneModel Model, string? Camera);

/// <summary>One shipped document no class claims, composed by the engine's Scene or by a generated subclass of its baseScene.</summary>
/// <param name="Camera">The fully qualified camera the document names, or null.</param>
internal readonly record struct DocumentOnlyScene(string DocumentName, GeneratedBase? Base, string? Camera);

/// <summary>One tile type class and the key it claims, which a palette entry's type names.</summary>
internal readonly record struct KeyedTileType(string Key, TileTypeModel Model);

/// <summary>The internal sealed scene a document's baseScene generates.</summary>
/// <param name="Base">The abstract scene it derives.</param>
internal readonly record struct GeneratedBase(string ClassName, SceneModel Base);

/// <summary>What <c>CapsuleScenes.g.cs</c> holds, resolved from every scene, camera and tile type class and every shipped document.</summary>
/// <param name="Generates">Whether the assembly gets the file at all: only a logic assembly does.</param>
/// <param name="Registered">Every sound scene class, one per document, those with no document first.</param>
/// <param name="DocumentOnly">Every document no class claims, sorted by key.</param>
/// <param name="Applied">
/// Every scene class an applier is emitted for, sorted by name: each class that authors members and generated code
/// can name, plus each class a document composes.
/// </param>
/// <param name="TileTypes">Every tile type class by the key it keeps, valid or not, sorted by key.</param>
/// <param name="Lookups">The asset lookups the composing classes' authored members read through.</param>
/// <param name="Diagnostics">Every fault found resolving the plan.</param>
internal readonly record struct ScenePlan(
    bool Generates,
    EquatableArray<RegisteredScene> Registered,
    EquatableArray<DocumentOnlyScene> DocumentOnly,
    EquatableArray<SceneModel> Applied,
    EquatableArray<KeyedTileType> TileTypes,
    EquatableArray<AssetLookup> Lookups,
    EquatableArray<Diagnostic> Diagnostics)
{
    /// <summary>Every tile type a palette entry's type can compose.</summary>
    internal IEnumerable<KeyedTileType> Composed => TileTypes.Items.Where(static entry => entry.Model.Valid);
}
