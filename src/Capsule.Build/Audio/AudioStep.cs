using Capsule.Audio;
using Capsule.Build.Caching;

namespace Capsule.Build.Audio;

/// <summary>
/// Ships every audio source and declares it as an <c>AudioClip</c> carrying the duration and loop
/// region the build measured.
/// </summary>
/// <remarks>
/// A WAVE source of integer samples ships encoded as FLAC, and every other source as it was authored.
/// Nothing is read at run time to learn how long a sound is.
/// </remarks>
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
                string shipped = AudioClip.Shipped(source.Extension, measured.FloatSamples);
                if (shipped == AudioClip.Flac)
                {
                    files.Write(source.Key + shipped, path => FlacEncoder.Encode(source.Path, path));
                }
                else
                {
                    files.Copy(source.Path, source.Key + shipped);
                }

                return measured;
            },
            DerivationCacheJsonContext.Default.Measurement))
        {
            pass.Assets.Index.Sound(clip, measured);
            pass.Declare(clip, measured, AudioMembers.Write);
        }
    }
}
