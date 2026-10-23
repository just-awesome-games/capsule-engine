namespace Capsule.Generators;

// Every engine, build and framework name the generator looks up. The generator references no engine
// assembly. The compiler cannot check these. The generator tests compile against the engine and do.
internal static class MetadataNames
{
    internal const string LogicRoleProperty = "build_property.CapsuleGameLogic";
    internal const string ShellRoleProperty = "build_property.CapsuleGameShell";
    internal const string RootNamespaceProperty = "build_property.RootNamespace";

    internal const string Entity = "Capsule.Scenes.Entity";
    internal const string Component = "Capsule.Scenes.Component";
    internal const string EntitySpawn = "Capsule.Scenes.EntitySpawn";
    internal const string TypeKeyAttribute = "Capsule.Scenes.TypeKeyAttribute";
    internal const string AuthorableAttribute = "Capsule.Scenes.AuthorableAttribute";
    internal const string Scene = "Capsule.Scenes.Scene";
    internal const string SceneContent = "Capsule.Scenes.SceneContent";
    internal const string TileMap = "Capsule.Tiles.TileMap";
    internal const string InputDriver = "Capsule.Input.IInputDriver";
    internal const string ScenePayload = "Capsule.Scenes.IScenePayload";
    internal const string CapsuleEngine = "Capsule.Runtime.CapsuleEngine";
    internal const string SaveKey = "Capsule.Persistence.SaveKey`1";

    // The engine's reserved scene document keys, one nested class of string constants per object.
    internal const string DocumentKeys = "Capsule.Scenes.SceneDocumentKeys+Document";
    internal const string EntryKeys = "Capsule.Scenes.SceneDocumentKeys+Entry";
    internal const string MemberObjectKeys = "Capsule.Scenes.SceneDocumentKeys+MemberObject";

    // What the build marks CapsuleAssets with.
    internal const string AssetAttribute = "Capsule.Generated.CapsuleGeneratedAssetAttribute";
    internal const string SceneDocumentKeyAttribute = "Capsule.Generated.CapsuleGeneratedSceneDocumentAttribute";

    // What a logic assembly's generated registry provider declares, and the shell reads.
    internal const string RegistryProviderAttribute = "Capsule.Generated.CapsuleGeneratedRegistryProviderAttribute";
    internal const string RegistryClaimAttribute = "Capsule.Generated.CapsuleGeneratedRegistryClaimAttribute";

    // What the build writes into a logic assembly with string tables, whose table the shell's boot passes and a
    // document's text keys resolve through.
    internal const string CapsuleStrings = "Capsule.Generated.CapsuleStrings";

    internal const string JsonConverterAttribute = "System.Text.Json.Serialization.JsonConverterAttribute";
    internal const string JsonConverter = "System.Text.Json.Serialization.JsonConverter`1";
    internal const string FlagsAttribute = "System.FlagsAttribute";
    internal const string ReadOnlyMemory = "System.ReadOnlyMemory`1";
}
