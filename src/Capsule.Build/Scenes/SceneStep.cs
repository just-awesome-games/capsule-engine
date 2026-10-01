using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
using Capsule.Build.Caching;
using Capsule.Build.Registry;
using Capsule.Scenes.Documents;
using Capsule.Tiles;

namespace Capsule.Build.Scenes;

/// <summary>
/// Every scene document, authored or imported, validated and re-emitted compact and gzipped
/// where it ships, and each key declared as a constant. The indented canonical form is the authoring format.
/// </summary>
internal static class SceneStep
{
    private const string Step = "scenes";

    /// <summary>The tool a hand-authored document's provenance names.</summary>
    internal const string ToolName = "native";

    private const char ByteOrderMark = '\uFEFF';

    internal static void Run(PipelinePass pass)
    {
        // A publish spends the time to ship the smallest documents. Any other build compresses fastest.
        CompressionLevel level = pass.Requests.Shipping ? CompressionLevel.SmallestSize : CompressionLevel.Fastest;
        string settings = pass.Configuration.TileSize is { } size ? $"gzip={level}; tileSize={size}" : $"gzip={level}";
        foreach ((Source document, string[] attributes) in pass.Each(
            Step,
            pass.Of(AssetType.Scenes),
            source => Derivation.Of(source, settings),
            (source, files) =>
            {
                // An authored file need not be canonical, and the hash is over its bytes. An editor may
                // add a byte order mark, which the JSON reader would find where it expects a brace.
                byte[] bytes = File.ReadAllBytes(source.Path);
                string json = Encoding.UTF8.GetString(bytes).TrimStart(ByteOrderMark);
                SceneDocument document = Import(source.Path, bytes, json, pass.Configuration.TileSize);
                files.Write(source.Key + ShippedSceneDocument.Extension, path => ShippedSceneDocument.Write(document, path, level));

                return SceneMembers.Attributes(document, source.Key, source.Path, json);
            },
            DerivationCacheJsonContext.Default.StringArray))
        {
            pass.Assets.Beside(GeneratedAttributes.SceneDocument);
            pass.Declare(document, attributes, SceneMembers.Write);
        }
    }

    private static SceneDocument Import(string documentPath, byte[] sourceBytes, string json, int? tileSize)
    {
        SceneDocument authored = SceneDocumentFile.Parse(json);

        if (tileSize is { } declared)
        {
            foreach (SceneDocumentEntry entry in authored.Entries)
            {
                if (entry.TileMap is { Grid.TileSize: var actual } && actual != declared)
                {
                    throw new SceneDocumentFormatException(
                        $"the scene document has {actual}px tiles but the game declares {declared}px. Set tileSize to {declared} on every tile-map entry, or change the tile size the build project passes to WithTileSize.");
                }
            }
        }

        // A document that arrives stamped was imported by an authoring module, and its block names
        // the file a person edited. Re-stamping it would name the intermediate instead.
        SceneDocumentSource source = authored.Source ?? new(
            ToolName,
            documentPath.Replace('\\', '/'),
            Convert.ToHexStringLower(SHA256.HashData(sourceBytes)));

        return new SceneDocument(Keyed(authored.Entries), authored.NextEntityId, source, authored.Settings);
    }

    // A texture is reached by its key however the document spelled it, so what is re-emitted and
    // what the runtime loads is the path the build ships it at.
    private static SceneDocumentEntry[] Keyed(ReadOnlySpan<SceneDocumentEntry> entries)
    {
        SceneDocumentEntry[] keyed = new SceneDocumentEntry[entries.Length];

        for (int i = 0; i < entries.Length; i++)
        {
            keyed[i] = entries[i].TileMap is { Grid: { Texture: { } texture } grid } map
                ? map with { Grid = Regrid(grid, Keyed(texture)) }
                : entries[i];
        }

        return keyed;
    }

    private static TextureHandle Keyed(TextureHandle texture) =>
        new(
            Keys.Of(texture.Name, $"has a tile-map entry drawing from texture \"{texture.Name}{texture.Extension}\""),
            texture.Extension.ToLowerInvariant());

    private static TileGrid Regrid(TileGrid grid, TextureHandle texture) =>
        texture == grid.Texture
            ? grid
            : new TileGrid(
                grid.TileSize,
                grid.Width,
                grid.Height,
                grid.TileTypes.ToArray(),
                grid.Tiles.ToArray(),
                texture,
                grid.Columns,
                grid.Transforms.ToArray(),
                grid.Authored);
}
