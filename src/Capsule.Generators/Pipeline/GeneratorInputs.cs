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

/// <summary>What both registry resolvers read beside their own models: the project and what the build declared.</summary>
/// <param name="Documents">Every scene document the build shipped.</param>
/// <param name="Assets">Every texture and sound the build declared.</param>
internal readonly record struct RegistryInputs(
    bool IsLogicAssembly,
    string RootNamespace,
    EquatableArray<SceneDocumentModel> Documents,
    EquatableArray<AssetModel> Assets);

/// <summary>What the entity resolver reads.</summary>
/// <param name="EngineTileMap">The engine's tile map, which every logic assembly registers under its key.</param>
internal readonly record struct EntityInputs(EquatableArray<EntityModel> Models, EntityModel? EngineTileMap, RegistryInputs Shared);

/// <summary>What the scene resolver reads.</summary>
/// <param name="EngineScene">The engine's plain Scene, which a document naming no class composes.</param>
internal readonly record struct SceneInputs(EquatableArray<SceneModel> Models, SceneModel? EngineScene, RegistryInputs Shared);
