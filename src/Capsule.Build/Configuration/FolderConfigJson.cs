namespace Capsule.Build.Configuration;

/// <summary>A folder's <c>.config.json</c>: one block per asset kind that has settings.</summary>
internal sealed class FolderConfigJson
{
    /// <summary>The kinds with settings, as a refusal names them.</summary>
    internal const string Kinds = "Only textures have settings.";

    /// <summary>What a folder file holds, as a refusal describes it.</summary>
    internal const string Shape = "A folder's .config.json is keyed by asset kind, as { \"texture\": { \"atlas\": \"game\" } }. "
        + Kinds + " " + TextureConfigJson.Menu;

    public TextureConfigJson? Texture { get; set; }
}
