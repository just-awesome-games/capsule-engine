using System.Text;
using Capsule.Audio;
using Capsule.Build.Registry;

namespace Capsule.Build.Audio;

/// <summary>Writes a sound's member: an <c>AudioClip</c> carrying its measured duration and any loop region.</summary>
internal static class AudioMembers
{
    internal static void Write(StringBuilder code, string indent, string identifier, Source audio, AudioProbe.Measurement measured)
    {
        (double duration, AudioLoopRegion loop) = measured;

        code.Append(indent).Append("/// <summary><c>").Append(audio.Key)
            .Append(audio.Extension).Append("</c>, ").Append(Literal.Number(duration)).Append(" seconds long");

        if (loop.HasRegion)
        {
            code.Append(", looping ").Append(Literal.Number(loop.StartSeconds)).Append(" to ")
                .Append(Literal.Number(loop.EndSeconds)).Append(" seconds");
        }

        code.AppendLine(".</summary>");
        code.Append(indent).Append('[').Append(GeneratedAttributes.AssetName).Append('(')
            .Append(Literal.Of(audio.Key + audio.Extension)).AppendLine(")]");
        code.Append(indent).Append("public static ").Append(GeneratedTypes.AudioClip).Append(' ').Append(identifier)
            .Append(" => new ").Append(GeneratedTypes.AudioClip).Append('(').Append(Literal.Of(audio.Key)).Append(", ")
            .Append(Literal.Of(audio.Extension)).Append(", ").Append(Literal.Of(duration));

        // Omitted when the source authored no region, so the constructor's default stands.
        if (loop.HasRegion)
        {
            code.Append(", new ").Append(GeneratedTypes.AudioLoopRegion).Append('(').Append(Literal.Of(loop.StartSeconds))
                .Append(", ").Append(Literal.Of(loop.EndSeconds)).Append(')');
        }

        code.AppendLine(");");
    }
}
