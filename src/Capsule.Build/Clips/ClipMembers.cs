using System.Text;
using Capsule.Build.Registry;

namespace Capsule.Build.Clips;

/// <summary>Writes a clip's member: one shared <c>ValueClip&lt;float&gt;</c> of its keys.</summary>
internal static class ClipMembers
{
    internal static void Write(StringBuilder code, string indent, string identifier, Source clip, Clip document)
    {
        string type = GeneratedTypes.ValueClip + "<float>";
        code.Append(indent).Append("/// <summary><c>").Append(clip.Key).Append(clip.Extension).Append("</c>: ")
            .Append(Literal.Of(document.Keys.Length)).Append(document.Keys.Length == 1 ? " key, " : " keys, ")
            .Append(Literal.Of(document.Keys.Sum(static key => key.Ticks))).Append(" ticks, ")
            .Append(document.Blend == ClipBlend.Linear ? "linear, " : "stepped, ")
            .Append(document.Loop ? "looping" : "played once").AppendLine(".</summary>");

        // A property with a field initializer, not an expression body. A clip is identified by
        // instance, and every animator playing it reads the same one.
        code.Append(indent).Append("public static ").Append(type).Append(' ').Append(identifier).Append(" { get; } = ")
            .Append(document.Blend == ClipBlend.Linear ? GeneratedTypes.ValueClip + ".Linear(" : "new " + type + "(")
            .Append("new (float, int)[] { ")
            .AppendJoin(", ", document.Keys.Select(static key => $"({Literal.Of(key.Value)}, {Literal.Of(key.Ticks)})"))
            .Append(" }, ").Append(document.Loop ? "true" : "false").AppendLine(");");
    }
}
