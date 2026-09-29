namespace Capsule.Generators;

/// <summary>What the project declares and references, which decides every file the generator writes.</summary>
/// <param name="ScenesReferenced">Whether <c>Capsule.Scenes</c> is referenced, without which a logic assembly has no registry to hold.</param>
/// <param name="RuntimeReferenced">Whether <c>Capsule.Runtime</c> is referenced, without which a shell has no engine to boot.</param>
internal readonly record struct ProjectConfiguration(
    GeneratorRole Role,
    bool ScenesReferenced,
    bool RuntimeReferenced,
    string AssemblyName)
{
    /// <summary>Whether the game's registries are generated here.</summary>
    internal bool IsLogicAssembly => Role == GeneratorRole.Logic && ScenesReferenced;

    /// <summary>Whether the game's entry point is generated here.</summary>
    internal bool IsShellAssembly => Role == GeneratorRole.Shell && RuntimeReferenced;
}

/// <summary>What the entity resolver reads.</summary>
internal readonly record struct EntityInputs(
    EquatableArray<EntityModel> Models,
    bool IsLogicAssembly,
    string RootNamespace,
    EquatableArray<SceneDocumentModel> Documents,
    EquatableArray<AssetModel> Assets);

/// <summary>What the scene resolver reads.</summary>
internal readonly record struct SceneInputs(
    EquatableArray<SceneModel> Models,
    bool IsLogicAssembly,
    string RootNamespace,
    EquatableArray<SceneDocumentModel> Documents,
    EquatableArray<CameraModel> Cameras,
    EquatableArray<TileTypeModel> TileTypes,
    EquatableArray<AssetModel> Assets);

/// <summary>What the placement check reads: each document's entries and own properties, and the classes and assets they name.</summary>
internal readonly record struct PlacementInputs(
    EntityPlan Entities,
    ScenePlan Scenes,
    EquatableArray<SceneDocumentModel> Documents,
    EquatableArray<AssetModel> Assets);

/// <summary>What the document claim check reads.</summary>
internal readonly record struct DocumentClaimInputs(
    ScenePlan Scenes,
    EquatableArray<SceneDocumentModel> Documents);

/// <summary>What a logic assembly's driver registry is written from.</summary>
internal readonly record struct InputDriverInputs(
    InputDriverPlan Drivers,
    bool IsLogicAssembly);

/// <summary>What the boot resolver reads: the referenced registries, and the drivers the shell declares itself.</summary>
internal readonly record struct BootInputs(
    BootModel Boot,
    InputDriverPlan Drivers);
