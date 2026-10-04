using System.IO.Compression;
using Capsule.Build.Caching;
using Capsule.Build.Registry;
using Capsule.Scenes.Documents;

namespace Capsule.Build.Scenes;

/// <summary>
/// Every scene document, authored or imported, validated and re-emitted compact and gzipped
/// where it ships, and each key declared as a constant.
/// </summary>
internal static class SceneStep
{
    private const string Step = "scenes";

    internal static void Run(PipelinePass pass)
    {
        // A publish spends the time to ship the smallest documents. Any other build compresses fastest.
        CompressionLevel level = pass.Requests.Shipping ? CompressionLevel.SmallestSize : CompressionLevel.Fastest;
        string settings = $"gzip={level}";
        foreach ((Source document, string[] attributes) in pass.Each(
            Step,
            pass.Of(AssetType.Scenes),
            source => Derivation.Of(source, settings),
            (source, files) =>
            {
                // Reading it as text drops any byte order mark an editor added.
                SceneDocument document = SceneDocument.Parse(File.ReadAllText(source.Path));
                files.Write(source.Key + ShippedSceneDocument.Extension, path => ShippedSceneDocument.Write(document, path, level));

                return SceneMembers.Attributes(document, source.Key);
            },
            DerivationCacheJsonContext.Default.StringArray))
        {
            pass.Assets.Beside(GeneratedAttributes.SceneDocument);
            pass.Declare(document, attributes, SceneMembers.Write);
        }
    }
}
