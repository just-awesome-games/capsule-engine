using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Capsule.Assets;

namespace Capsule.Build.Atlases;

/// <summary>A <c>&lt;name&gt;.atlas.json</c>, which declares the atlas <c>&lt;name&gt;</c> and holds its own settings.</summary>
[Description("A <name>.atlas.json, which declares the atlas <name> and holds its own settings. {} takes every default.")]
internal sealed class AtlasConfigJson
{
    /// <summary>The page extent when the file sets none.</summary>
    internal const int DefaultMaxSize = 4096;

    /// <summary>The largest page extent a file may set.</summary>
    internal const int LargestMaxSize = 8192;

    /// <summary>What an atlas file holds, as a refusal describes it.</summary>
    internal const string Shape = "An atlas file holds only the atlas's own settings, as {} or { \"maxSize\": 2048 }. "
        + "\"maxSize\" is the largest page extent, a power of two up to 8192, and 4096 by default.";

    // Read and ignored.
    [JsonPropertyName(SchemaKeyConverter.Key)]
    [JsonConverter(typeof(SchemaKeyConverter))]
    public string? Schema { get; set; }

    /// <summary>The largest extent a page reaches on either axis, or null for <see cref="DefaultMaxSize"/>.</summary>
    [Description("The largest extent a page reaches on either axis, in texels. A power of two up to 8192.")]
    [DefaultValue(DefaultMaxSize)]
    [AllowedValues(1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, LargestMaxSize)]
    public int? MaxSize { get; set; }
}
