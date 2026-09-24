using System.Security.Cryptography;
using System.Text;
using Capsule.Assets;
using Capsule.Build.Registry;
using Capsule.Scenes.Documents;
using Capsule.Tiles;

namespace Capsule.Build.Scenes;

/// <summary>
/// Every scene document, authored or derived by a module, validated and re-emitted canonically where
/// it ships, and each key declared as a constant.
/// </summary>
internal static class SceneStep
{
    /// <summary>The tool a hand-authored document's provenance names.</summary>
    internal const string ToolName = "native";

    private const char ByteOrderMark = '\uFEFF';

    // What marks a constant as a shipped scene document. The generator finds it by this name.
    internal const string DocumentAttributeName = "CapsuleGeneratedSceneDocument";

    internal const string DocumentAttribute = """
            /// <summary>A shipped scene document, with the baseScene and camera the build read from it. Generated code.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Field)]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            internal sealed class CapsuleGeneratedSceneDocumentAttribute : global::System.Attribute
            {
                /// <summary>The key of the abstract scene the document derives from, or null.</summary>
                public string? BaseScene { get; set; }

                /// <summary>The key of the camera the document installs, or null.</summary>
                public string? Camera { get; set; }
            }

        """;

    internal static void Build(BuildPass pass)
    {
        List<(Source Source, SceneDocument Document)> documents = pass.Each(
            pass.Of(AssetType.Scenes),
            source =>
            {
                SceneDocument document = Import(source.Path, pass.Requests.TileSize);
                string shipped = pass.Shipped.Claim(source.Key + AssetType.Scenes.Extensions[0]);
                AtomicFile.Write(shipped, path => SceneDocumentFile.Save(document, path));
                pass.Output.WriteLine($"scenes: {source.Path} -> {source.Key}");

                return document;
            });

        if (documents.Count > 0)
        {
            pass.Assets.Beside(DocumentAttribute);
        }

        // The generator composes the scene registry from these constants, so each carries the two
        // fields it needs and cannot read out of the document itself.
        foreach ((Source document, SceneDocument scene) in documents)
        {
            string[] named =
            [
                .. scene.Settings.BaseScene is { } baseScene ? [$"BaseScene = {Literal.Of(baseScene)}"] : Array.Empty<string>(),
                .. scene.Settings.Camera is { } camera ? [$"Camera = {Literal.Of(camera)}"] : Array.Empty<string>(),
            ];

            pass.Declare(document, (source, indent, identifier) =>
            {
                source.Append(indent).Append("/// <summary>The scene document <c>").Append(document.Key).AppendLine("</c>.</summary>");
                source.Append(indent).Append('[').Append(DocumentAttributeName)
                    .Append(named.Length > 0 ? "(" + string.Join(", ", named) + ")" : string.Empty).AppendLine("]");
                source.Append(indent).Append("public const string ").Append(identifier).Append(" = ").Append(Literal.Of(document.Key)).AppendLine(";");
            });
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
                        $"the scene document has {actual}px tiles but the game declares {declared}px. Set tileSize to {declared} on every tile-map entry, or change CapsuleTileSize.");
                }
            }
        }

        // A document that arrives stamped was derived by an authoring module, and its block names
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
                grid.Columns);
}
