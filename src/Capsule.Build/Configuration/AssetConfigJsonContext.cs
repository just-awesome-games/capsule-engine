using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Capsule.Assets;
using Capsule.Build.Atlases;
using Capsule.Build.Textures;

namespace Capsule.Build.Configuration;

// Reflection-based serialization is off solution-wide. A kind, setting or value the format does not
// declare fails the file.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(FolderConfigJson))]
[JsonSerializable(typeof(TextureConfigJson))]
[JsonSerializable(typeof(AtlasConfigJson))]
[JsonSerializable(typeof(TextureFormatSetting))]
[JsonSerializable(typeof(TextureSamplingSetting))]
internal sealed partial class AssetConfigJsonContext : JsonSerializerContext
{
    /// <summary>What a refusal calls a config or atlas file's members and values.</summary>
    internal const string Members = "kind, setting or value";

    /// <summary>What a refusal of a null in a config or atlas file says to write instead.</summary>
    /// <remarks>A present null would read as an omitted setting and silently inherit.</remarks>
    internal const string NullFix = "Omit a setting to inherit it, or write its default, such as false for \"atlas\".";

    private static JsonTypeInfo<FolderConfigJson>? LazyFolderFile;

    /// <summary>A folder file, whose <c>"texture"</c> block is refused a <c>"$schema"</c> as any unknown setting is.</summary>
    internal static JsonTypeInfo<FolderConfigJson> FolderFile => LazyFolderFile ??= (JsonTypeInfo<FolderConfigJson>)new JsonSerializerOptions(Default.Options)
    {
        TypeInfoResolver = Default.WithAddedModifier(static type =>
        {
            if (type.Type != typeof(TextureConfigJson))
            {
                return;
            }

            for (int i = type.Properties.Count - 1; i >= 0; i--)
            {
                if (type.Properties[i].Name == SchemaKeyConverter.Key)
                {
                    type.Properties.RemoveAt(i);
                }
            }
        }),
    }.GetTypeInfo(typeof(FolderConfigJson));

    /// <summary>Reads <paramref name="file"/> as one <typeparamref name="T"/> of the config formats.</summary>
    /// <param name="shape">What the file holds and every setting it may set, for a refusal.</param>
    /// <exception cref="FormatException">The JSON is malformed, or a kind, setting or value is not one this build knows.</exception>
    internal static T Read<T>(Source file, JsonTypeInfo<T> type, string shape) =>
        StrictJson.Read(file.Path, type, Members, NullFix, shape);
}
