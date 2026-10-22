using Capsule.Build.Caching;

namespace Capsule.Build.Clips;

/// <summary>
/// Reads every value clip and declares it as one shared <c>ValueClip&lt;float&gt;</c>. A misspelt clip
/// is a compile error, and no clip ships beside the executable.
/// </summary>
internal static class ClipStep
{
    private const string Step = "clips";

    internal static void Run(PipelinePass pass)
    {
        foreach ((Source clip, Clip document) in pass.Each(
            Step,
            pass.Of(AssetType.Clips),
            source => Derivation.Of(source),
            (source, _) => ClipFile.Read(source.Path),
            DerivationCacheJsonContext.Default.Clip))
        {
            pass.Declare(clip, document, ClipMembers.Write);
        }
    }
}
