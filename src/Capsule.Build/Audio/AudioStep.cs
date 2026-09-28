using Capsule.Build.Registry;

namespace Capsule.Build.Audio;

/// <summary>
/// Ships every audio source as it was authored and declares it as an <c>AudioClip</c> carrying the
/// duration and loop region the build measured. Nothing is read at run time to learn how long a
/// sound is.
/// </summary>
internal static class AudioStep
{
    internal static void Run(BuildPass pass)
    {
        foreach ((Source clip, AudioProbe.Measurement measured) in pass.Each(
            pass.Of(AssetType.Audio),
            source =>
            {
                AudioProbe.Measurement measured = AudioProbe.Measure(source.Path);
                pass.Shipped.Copy(source.Path, source.Key + source.Extension);
                pass.Progress("audio", source);

                return measured;
            }))
        {
            pass.Beside(GeneratedAttributes.Asset);
            pass.Declare(clip, measured, AudioMembers.Write);
        }
    }
}
