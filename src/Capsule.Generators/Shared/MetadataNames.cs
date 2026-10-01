namespace Capsule.Generators;

// Every engine, build and framework name the generator looks up. The generator references no engine
// assembly, so the compiler cannot check these. The generator tests compile against the engine and do.
internal static class MetadataNames
{
    internal const string LogicRoleProperty = "build_property.CapsuleGameLogic";
    internal const string ShellRoleProperty = "build_property.CapsuleGameShell";
    internal const string RootNamespaceProperty = "build_property.RootNamespace";

    internal const string Entity = "Capsule.Scenes.Entity";
    internal const string EntitySpawn = "Capsule.Scenes.Spawning.EntitySpawn";
    internal const string SpawnTypeAttribute = "Capsule.Scenes.Spawning.SpawnTypeAttribute";
    internal const string AuthorableAttribute = "Capsule.Scenes.AuthorableAttribute";
    internal const string Scene = "Capsule.Scenes.Scene";
    internal const string SceneContent = "Capsule.Scenes.SceneContent";
    internal const string SceneDocumentAttribute = "Capsule.Scenes.SceneDocumentAttribute";
    internal const string Camera = "Capsule.Scenes.Camera";
    internal const string TileType = "Capsule.Tiles.TileType";
    internal const string InputDriver = "Capsule.Input.IInputDriver";
    internal const string CapsuleEngine = "Capsule.Runtime.CapsuleEngine";
    internal const string SaveKey = "Capsule.Persistence.SaveKey`1";

    // What the build marks CapsuleAssets with. A placement or tile type attribute is matched by its simple name.
    internal const string AssetAttribute = "Capsule.Generated.CapsuleGeneratedAssetAttribute";
    internal const string SceneDocumentKeyAttribute = "Capsule.Generated.CapsuleGeneratedSceneDocumentAttribute";
    internal const string PlacementAttributeName = "CapsuleGeneratedPlacementAttribute";
    internal const string TileTypeAttributeName = "CapsuleGeneratedTileTypeAttribute";

    // What a logic assembly's generated registry provider declares, and the shell reads.
    internal const string RegistryProviderAttribute = "Capsule.Generated.CapsuleGeneratedRegistryProviderAttribute";
    internal const string RegistryClaimAttribute = "Capsule.Generated.CapsuleGeneratedRegistryClaimAttribute";

    internal const string JsonConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";
    internal const string JsonConverter = "System.Text.Json.Serialization.JsonConverter`1";
    internal const string FlagsAttribute = "System.FlagsAttribute";
}
