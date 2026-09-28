using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Capsule.Assets;
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
    /// <summary>The tool a hand-authored document's provenance names.</summary>
    internal const string ToolName = "native";

    private const char ByteOrderMark = '\uFEFF';

    internal static void Run(BuildPass pass)
    {
        foreach ((Source document, (SceneDocument Scene, string Json) model) in pass.Each(
            pass.Of(AssetType.Scenes),
            source =>
            {
                SceneDocument document = Import(source.Path, pass.Configuration.TileSize);
                string shipped = pass.Shipped.Claim(source.Key + ShippedSceneDocument.Extension, $"'{source.Path}'");
                AtomicFile.Write(shipped, path => ShippedSceneDocument.Write(document, path));
                pass.Progress("scenes", source);

                return (document, File.ReadAllText(source.Path));
            }))
        {
            pass.Beside(GeneratedAttributes.SceneDocument);
            pass.Declare(document, model, SceneMembers.Write);
        }
    }

    internal static SceneDocument Import(string documentPath, int? tileSize = null)
    {
        // Read as bytes, since the hash is over the source bytes and an authored file need not be
        // canonical. The format is written without a BOM, but an editor may add one and the JSON
        // reader would find it where it expects a brace.
        byte[] sourceBytes = File.ReadAllBytes(documentPath);
        SceneDocument authored = SceneDocumentFile.Parse(Encoding.UTF8.GetString(sourceBytes).TrimStart(ByteOrderMark));

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
                ? new TileMapPlacement(map.Id, Regrid(grid, Keyed(texture)), map.ZIndex, map.ScrollFactor)
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
                grid.Transforms.ToArray());
}
