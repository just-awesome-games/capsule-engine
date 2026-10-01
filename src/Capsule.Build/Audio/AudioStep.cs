using Capsule.Build.Caching;
using Capsule.Build.Registry;

namespace Capsule.Build.Audio;

/// <summary>
/// Ships every audio source as it was authored and declares it as an <c>AudioClip</c> carrying the
/// duration and loop region the build measured. Nothing is read at run time to learn how long a
/// sound is.
/// </summary>
internal static class AudioStep
{
    private const string Step = "audio";

    internal static void Run(PipelinePass pass)
    {
        foreach ((Source clip, AudioProbe.Measurement measured) in pass.Each(
            Step,
            pass.Of(AssetType.Audio),
            source => Derivation.Of(source),
            (source, files) =>
            {
                AudioProbe.Measurement measured = AudioProbe.Measure(source.Path);
                files.Copy(source.Path, source.Key + source.Extension);

                return measured;
            },
            DerivationCacheJsonContext.Default.Measurement))
        {
            pass.Assets.Beside(GeneratedAttributes.Asset);
            pass.Declare(clip, measured, AudioMembers.Write);
        }
    }
}
