using Capsule.Audio;
using Capsule.Build.Registry;

namespace Capsule.Build.Audio;

/// <summary>
/// Every audio source, shipped as it was authored and declared as an <c>AudioClip</c> carrying the
/// duration and loop region the build measured. Nothing is read at run time to learn how long a
/// sound is.
/// </summary>
internal static class AudioStep
{
    private const string ClipType = "global::Capsule.Audio.AudioClip";

    private const string RegionType = "global::Capsule.Audio.AudioLoopRegion";

    internal static void Build(BuildPass pass)
    {
        foreach ((Source clip, AudioProbe.Measurement measured) in pass.Each(
            pass.Of(AssetType.Audio),
            source =>
            {
                AudioProbe.Measurement measured = AudioProbe.Measure(source.Path);
                pass.Shipped.Copy(source.Path, source.Key + source.Extension);
                pass.Output.WriteLine($"audio: {source.Path} -> {source.Key}");

                return measured;
            }))
        {
            pass.Declare(clip, (source, indent, identifier) => AppendClip(source, indent, identifier, clip, measured));
        }
    }

    private static void AppendClip(
        System.Text.StringBuilder source,
        string indent,
        string identifier,
        Source audio,
        AudioProbe.Measurement measured)
    {
        (double duration, AudioLoopRegion loop) = measured;

        source.Append(indent).Append("/// <summary><c>").Append(audio.Key)
            .Append(audio.Extension).Append("</c>, ").Append(Literal.Number(duration)).Append(" seconds long");

        if (loop.HasRegion)
        {
            source.Append(", looping ").Append(Literal.Number(loop.StartSeconds)).Append(" to ")
                .Append(Literal.Number(loop.EndSeconds)).Append(" seconds");
        }

        source.AppendLine(".</summary>");
        source.Append(indent).Append("public static ").Append(ClipType).Append(' ').Append(identifier)
            .Append(" => new ").Append(ClipType).Append('(').Append(Literal.Of(audio.Key)).Append(", ")
            .Append(Literal.Of(audio.Extension)).Append(", ").Append(Literal.Of(duration));

        // Omitted when the source authored no region, so the constructor's default stands.
        if (loop.HasRegion)
        {
            source.Append(", new ").Append(RegionType).Append('(').Append(Literal.Of(loop.StartSeconds))
                .Append(", ").Append(Literal.Of(loop.EndSeconds)).Append(')');
        }

        source.AppendLine(");");
    }
}
