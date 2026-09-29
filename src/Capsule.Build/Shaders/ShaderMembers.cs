using System.Text;
using Capsule.Build.Registry;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>Writes a shader's member: a <c>Shader</c> holding the parameter table it compiled with.</summary>
internal static class ShaderMembers
{
    // A property with an initializer, so every read hands back the one instance materials batch by.
    internal static void Write(StringBuilder code, string indent, string identifier, Source shader, IReadOnlyList<ShaderParameter> parameters)
    {
        code.Append(indent).Append("/// <summary><c>").Append(shader.Key).Append(shader.Extension).Append("</c>");
        if (parameters.Count == 0)
        {
            code.Append(", with no parameters");
        }
        else
        {
            code.Append(", setting ");
            for (int i = 0; i < parameters.Count; i++)
            {
                code.Append(i == 0 ? string.Empty : ", ").Append("<c>").Append(parameters[i].Name).Append("</c>");
            }
        }

        code.AppendLine(".</summary>");
        code.Append(indent).Append("public static ").Append(GeneratedTypes.Shader).Append(' ').Append(identifier)
            .Append(" { get; } = new ").Append(GeneratedTypes.Shader).Append('(').Append(Literal.Of(shader.Key));

        foreach (ShaderParameter parameter in parameters)
        {
            code.Append(", new ").Append(GeneratedTypes.ShaderParameter).Append('(').Append(Literal.Of(parameter.Name))
                .Append(", ").Append(GeneratedTypes.ShaderParameterKind).Append('.').Append(parameter.Kind).Append(')');
        }

        code.AppendLine(");");
    }
}
