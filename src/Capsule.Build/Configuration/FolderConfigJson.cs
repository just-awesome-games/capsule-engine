using System.ComponentModel;
using System.Text.Json.Serialization;
using Capsule.Assets;
using Capsule.Build.Textures;

namespace Capsule.Build.Configuration;

/// <summary>A folder's <c>.config.json</c>: one block per asset kind that has settings.</summary>
[Description("A folder's .config.json, keyed by asset kind. It configures every asset in its folder and in every folder below it.")]
internal sealed class FolderConfigJson
{
    /// <summary>The kinds with settings, as a refusal names them.</summary>
    internal const string Kinds = "Only textures have settings.";

    /// <summary>What a folder file holds, as a refusal describes it.</summary>
    internal const string Shape = "A folder's .config.json is keyed by asset kind, as { \"texture\": { \"atlas\": \"game\" } }. "
        + Kinds + " " + TextureConfigJson.Menu;

    // Read and ignored.
    [JsonPropertyName(SchemaKeyConverter.Key)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }

    [Description("The texture settings for every texture in this folder and in every folder below it. A nearer file's value replaces this one.")]
    public TextureConfigJson? Texture { get; set; }
}
