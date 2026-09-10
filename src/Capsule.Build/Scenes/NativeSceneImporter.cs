using System.Security.Cryptography;
using System.Text;
using Capsule.Assets;
using Capsule.Generators;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Tiles;

namespace Capsule.Build.Scenes;

internal static class NativeSceneImporter
{
    internal const string ToolName = "native";

    private const char ByteOrderMark = '\uFEFF';

    internal static SceneDocument Import(string documentPath, int? tileSize = null)
    {
        // Read as bytes, because the hash is over the source bytes: an authored file is under no
        // obligation to be canonical. The format is written without a BOM, but an editor may add
        // one and the JSON reader would find it where it expects a brace.
        byte[] sourceBytes = File.ReadAllBytes(documentPath);
        SceneDocument authored = SceneDocumentFile.Parse(Encoding.UTF8.GetString(sourceBytes).TrimStart(ByteOrderMark));

        if (tileSize is { } declared)
        {
            foreach (SceneDocumentEntry entry in authored.Entries)
            {
                if (entry.TileMap is { Grid.TileSize: var actual } && actual != declared)
                {
                    throw new SceneDocumentFormatException(
                        $"the scene document has {actual}px tiles but the game declares {declared}px; set tileSize to {declared} on every tile-map entry, or change CapsuleTileSize.");
                }
            }
        }

        // A document that arrives stamped was derived by an authoring module, and its block names
        // the file a person edited; re-stamping it would name the intermediate instead.
        SceneDocumentSource source = authored.Source ?? new(
            ToolName,
            documentPath.Replace('\\', '/'),
            Convert.ToHexStringLower(SHA256.HashData(sourceBytes)));

        return new SceneDocument(Keyed(authored.Entries), authored.NextEntityId, source);
    }

    // However a document spelled a texture, a texture is reached by its key, so what is re-emitted
    // and what the runtime loads is the path the build ships it at.
    private static SceneDocumentEntry[] Keyed(ReadOnlySpan<SceneDocumentEntry> entries)
    {
        SceneDocumentEntry[] keyed = new SceneDocumentEntry[entries.Length];

        for (int i = 0; i < entries.Length; i++)
        {
            keyed[i] = entries[i].TileMap is { Grid: { Texture: { } texture } grid } map
                ? new TileMapPlacement(map.Id, Regrid(grid, Keyed(texture)), map.ZIndex)
                : entries[i];
        }

        return keyed;
    }

    private static TextureHandle Keyed(TextureHandle texture) =>
        TypeNaming.NormalizeKey(texture.Name, out string? rejected) is { } key
            ? new TextureHandle(key, texture.Extension)
            : throw new SceneDocumentFormatException(
                $"a tile-map entry's grid draws from texture \"{texture.Name}{texture.Extension}\", whose \"{rejected}\" is no C# name; every segment of a texture path is letters, digits, '-' and '_', and does not start with a digit.");

    private static TileGrid Regrid(TileGrid grid, TextureHandle texture) =>
        string.Equals(texture.Name, grid.Texture!.Value.Name, StringComparison.Ordinal)
            ? grid
            : new TileGrid(
                grid.TileSize,
                grid.Width,
                grid.Height,
                grid.TileTypes.ToArray(),
                grid.Tiles.ToArray(),
                texture,
                grid.Columns);
}
