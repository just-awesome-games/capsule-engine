using System.Text;
using Capsule.Build.Registry;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>Writes a shader's member: a <c>Shader</c> holding the parameter table it compiled with.</summary>
internal static class ShaderMembers
{
    // A property with an initializer. Every read hands back the one instance materials batch by.
    internal static void Write(StringBuilder code, string indent, string identifier, Source shader, IReadOnlyList<ShaderParameter> parameters)
    {
        code.Append(indent).Append("/// <summary><c>").Append(shader.Key).Append(shader.Extension).Append("</c>");
        if (parameters.Count == 0)
        {
            code.Append(", with no parameters");
        }
        else
        {
            AppendNames(code, ", setting ", parameters, global: false);
            AppendNames(code, parameters.Any(static parameter => !parameter.Global) ? " and reading the scene globals " : ", reading the scene globals ", parameters, global: true);
        }

        code.AppendLine(".</summary>");
        code.Append(indent).Append("public static ").Append(GeneratedTypes.Shader).Append(' ').Append(identifier)
            .Append(" { get; } = new ").Append(GeneratedTypes.Shader).Append('(').Append(Literal.Of(shader.Key));

        foreach (ShaderParameter parameter in parameters)
        {
            code.Append(", new ").Append(GeneratedTypes.ShaderParameter).Append('(').Append(Literal.Of(parameter.Name))
                .Append(", ").Append(GeneratedTypes.ShaderParameterKind).Append('.').Append(parameter.Kind)
                .Append(parameter.Global ? ", true)" : ")");
        }

        code.AppendLine(");");
    }

    // The parameters of one scope, as a prefixed list. Nothing when the shader has none of that scope.
    private static void AppendNames(StringBuilder code, string prefix, IReadOnlyList<ShaderParameter> parameters, bool global)
    {
        bool first = true;
        foreach (ShaderParameter parameter in parameters)
        {
            if (parameter.Global != global)
            {
                continue;
            }

            code.Append(first ? prefix : ", ").Append("<c>").Append(parameter.Name).Append("</c>");
            first = false;
        }
    }
}
