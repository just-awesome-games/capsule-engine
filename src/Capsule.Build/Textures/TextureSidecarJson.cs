using System.ComponentModel;
using System.Text.Json.Serialization;
using Capsule.Assets;

namespace Capsule.Build.Textures;

/// <summary>A <c>&lt;file&gt;.&lt;ext&gt;.config.json</c>: one texture's settings at the root of its own file.</summary>
/// <remarks>Only a file's root may name its schema, so a folder file's <c>"texture"</c> block is the base class.</remarks>
[Description("A <file>.<ext>.config.json holding the settings of the one texture it names beside it. A setting it omits takes its value from the nearest folder .config.json that sets it.")]
internal sealed class TextureSidecarJson : TextureConfigJson
{
    // Read and ignored.
    [JsonPropertyName(SchemaKeyConverter.Key)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }
}
