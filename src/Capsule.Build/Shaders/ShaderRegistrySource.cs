using System.Text;
using Capsule.Generators;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>One compiled shader: its key and the parameters a material can set on it, in declaration order.</summary>
internal readonly record struct CompiledShader(string Key, IReadOnlyList<ShaderParameter> Parameters);

/// <summary>
/// Renders every shader a game ships as one C# file the game compiles. Each shader becomes a typed
/// member carrying the parameter table the build read from its compiled effect, so a misspelt shader
/// is a compile error and a misspelt parameter throws where the material is written.
/// </summary>
internal static class ShaderRegistrySource
{
    /// <summary>The generated class every shader is declared under.</summary>
    internal const string RegistryClass = "Shaders";

    private const string Domain = "shaders";

    private const string ShaderType = "global::Capsule.Rendering.Shader";

    private const string ParameterType = "global::Capsule.Rendering.ShaderParameter";

    private const string KindType = "global::Capsule.Rendering.ShaderParameterKind";

    /// <summary>
    /// The C# text declaring <paramref name="shaders"/>, ordered by key, the same on every machine. A
    /// shader's key is its path under the shaders root, and each directory in it becomes a nested
    /// class.
    /// </summary>
    /// <param name="shaders">Every compiled shader.</param>
    /// <param name="key">The key that could not be declared, when one could not.</param>
    /// <param name="because">Why it could not be declared.</param>
    /// <returns>The generated source, or null when a key names something C# would refuse.</returns>
    internal static string? Render(IReadOnlyList<CompiledShader> shaders, out string? key, out string? because)
    {
        ArgumentNullException.ThrowIfNull(shaders);

        List<CompiledShader> ordered = [.. shaders];
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        RegistryDomain<CompiledShader> registry = new(
            RegistryClass,
            Domain,
            ShaderType,
            "shader",
            "Every shader this game ships, with the parameters the build read from it.",
            AppendShader);

        string? refused = null;
        RegistryClaimCheck<CompiledShader> claimable = RegistryClaims.Check<CompiledShader>(
            refusal => refused = Refusals.Because(refusal));

        foreach (CompiledShader shader in ordered)
        {
            if (registry.Add(shader.Key, shader.Key, shader, claimable))
            {
                continue;
            }

            key = shader.Key;
            because = $"is keyed \"{shader.Key}\", {refused}";

            return null;
        }

        key = null;
        because = null;

        StringBuilder source = RegistryFile.Open();
        registry.Append(source, "        ");

        return RegistryFile.Close(source);
    }

    // A property with an initializer, so every read hands back the one instance materials batch by.
    private static void AppendShader(StringBuilder source, string indent, string identifier, CompiledShader shader)
    {
        source.Append(indent).Append("/// <summary><c>").Append(Domain).Append('/').Append(shader.Key).Append(".fx</c>");
        if (shader.Parameters.Count == 0)
        {
            source.Append(", with no parameters");
        }
        else
        {
            source.Append(", setting ");
            for (int i = 0; i < shader.Parameters.Count; i++)
            {
                source.Append(i == 0 ? string.Empty : ", ").Append("<c>").Append(shader.Parameters[i].Name).Append("</c>");
            }
        }

        source.AppendLine(".</summary>");
        source.Append(indent).Append("public static ").Append(ShaderType).Append(' ').Append(identifier)
            .Append(" { get; } = new ").Append(ShaderType).Append('(').Append(Literal(shader.Key));

        foreach (ShaderParameter parameter in shader.Parameters)
        {
            source.Append(", new ").Append(ParameterType).Append('(').Append(Literal(parameter.Name))
                .Append(", ").Append(KindType).Append('.').Append(parameter.Kind).Append(')');
        }

        source.AppendLine(");");
    }

    private static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
