using System.IO.Compression;
using Capsule.Build.Caching;
using Capsule.Build.Registry;
using Capsule.Scenes;
using Capsule.Tiles;

namespace Capsule.Build.Scenes;

/// <summary>
/// Every scene document, authored or imported, validated and re-emitted compact and gzipped
/// where it ships with each tile map's cells compiled into blocks, and each key declared as a constant.
/// </summary>
internal static class SceneStep
{
    private const string Step = "scenes";

    internal static void Run(PipelinePass pass)
    {
        // A publish spends the time to ship the smallest documents. Any other build compresses fastest.
        CompressionLevel level = pass.Requests.Shipping ? CompressionLevel.SmallestSize : CompressionLevel.Fastest;

        // A TileMap class added or removed changes the keys, and every document builds again once.
        string[] tileMaps = pass.Configuration.TileMaps ?? TileMapKeys.Built(pass.OutputDirectory);
        string settings = $"gzip={level};tiles={CompiledTiles.Version};tile-maps={string.Join(',', tileMaps)}";
        foreach ((Source document, string[] attributes) in pass.Each(
            Step,
            pass.Of(AssetType.Scenes),
            source => Derivation.Of(source, settings),
            (source, files) =>
            {
                // Reading it as text drops any byte order mark an editor added.
                SceneDocument document = SceneDocument.Parse(File.ReadAllText(source.Path));
                files.Write(source.Key + ShippedSceneDocument.Extension, path => ShippedSceneDocument.Write(CompiledTiles.Compile(document, tileMaps), path, level));

                return SceneMembers.Attributes(document, source.Key);
            },
            DerivationCacheJsonContext.Default.StringArray))
        {
            pass.Assets.Beside(GeneratedAttributes.SceneDocumentDeclaration);
            pass.Assets.Index.Scene(document);
            pass.Declare(document, attributes, SceneMembers.Write);
        }

        // Kept only by a run with no defect, whose every document ships with them. A run with any builds them again.
        if (pass.Failures == 0)
        {
            TileMapKeys.KeepBuilt(pass.OutputDirectory, tileMaps);
        }
    }
}
