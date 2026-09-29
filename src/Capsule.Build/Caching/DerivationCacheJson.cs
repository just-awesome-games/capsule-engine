using System.Text.Json;
using System.Text.Json.Serialization;
using Capsule.Build.Audio;
using Capsule.Build.Fonts;
using Capsule.Build.Shaders;
using Capsule.Build.Sheets;

namespace Capsule.Build.Caching;

/// <summary>The cache's file, <c>derivation-cache.json</c>, as one run writes it for the next.</summary>
internal sealed class DerivationCacheJson
{
    /// <summary>When the run started, on the output volume's clock, UTC.</summary>
    public DateTime Started { get; set; }

    /// <summary>Every file a derivation read, by its path relative to the project.</summary>
    public Dictionary<string, FileRecordJson> Files { get; set; } = [];

    /// <summary>Every derivation that succeeded, by its step and then its name.</summary>
    public Dictionary<string, Dictionary<string, DerivationJson>> Derivations { get; set; } = [];
}

/// <summary>One successful derivation: what it was stamped from, what it shipped and the facts it handed on.</summary>
internal sealed class DerivationJson
{
    /// <summary>The SHA-256 over the step, the build of <see cref="Tool"/>, <see cref="Settings"/> and each input's path and hash.</summary>
    public string Stamp { get; set; } = string.Empty;

    public string Tool { get; set; } = string.Empty;

    public string Settings { get; set; } = string.Empty;

    public string[] Inputs { get; set; } = [];

    /// <summary>Every file it read beyond <see cref="Inputs"/>, or null when it read none.</summary>
    public string[]? Read { get; set; }

    /// <summary>Every file it wrote, by its path below the root it writes under.</summary>
    public Dictionary<string, FileRecordJson> Outputs { get; set; } = [];

    /// <summary>What the step's later work reads in place of the source, or null when it reads nothing.</summary>
    public JsonElement? Facts { get; set; }
}

/// <summary>A file as a run found it. An output records no hash.</summary>
internal sealed class FileRecordJson
{
    public long Length { get; set; }

    /// <summary>Its last write time, UTC.</summary>
    public DateTime Written { get; set; }

    public string? Sha256 { get; set; }
}

/// <summary>The cache file, and every step's facts.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DerivationCacheJson))]
[JsonSerializable(typeof(AudioProbe.Measurement))]
[JsonSerializable(typeof(BmFontDescription))]
[JsonSerializable(typeof(Sheet))]
[JsonSerializable(typeof(ShaderFacts))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, FileRecordJson>))]
internal sealed partial class DerivationCacheJsonContext : JsonSerializerContext;
