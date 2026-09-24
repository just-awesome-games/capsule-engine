using System.Text;
using Capsule.Build.Registry;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>
/// Every shader: its fragment wrapped in the engine's template, compiled with the parameter table it
/// declares, shipped at its path as <c>.mgfx</c>, and declared as a <c>Shader</c>. A misspelt shader is a compile error, and a misspelt parameter throws
/// where the material is written.
/// </summary>
internal static class ShaderStep
{
    private const string CompiledExtension = ".mgfx";

    // Beside each composed source it was compiled from, not shipped: the parameter table, one
    // 'name|kind' per line, which an unchanged source reads back instead of compiling again.
    private const string ParametersExtension = ".parameters";

    private const string ShaderType = "global::Capsule.Rendering.Shader";

    private const string ParameterType = "global::Capsule.Rendering.ShaderParameter";

    private const string KindType = "global::Capsule.Rendering.ShaderParameterKind";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    internal static void Build(BuildPass pass)
    {
        List<Source> sources = [.. pass.Of(AssetType.Shaders)];
        if (sources.Count == 0)
        {
            return;
        }

        if (pass.Requests.ShaderTools is not { } tools)
        {
            pass.Fail(sources[0].Path, "the build named no shader tools. Restore the project, which downloads them for a game with shaders.");

            return;
        }

        foreach ((Source shader, List<ShaderParameter> parameters) in pass.Each(
            sources,
            source =>
            {
                string composed = ShaderTemplate.Compose(File.ReadAllText(source.Path), source.Path);
                List<ShaderParameter> parameters = CompileIfChanged(pass, tools, composed, source)
                    ?? throw new FormatException("did not compile. Its errors are reported above.");
                pass.Output.WriteLine($"shaders: {source.Path} -> {source.Key}");

                return parameters;
            }))
        {
            pass.Declare(shader, (source, indent, identifier) => AppendShader(source, indent, identifier, shader, parameters));
        }
    }

    /// <summary>
    /// Compiles the engine's own sprite shader, the template around its default fragment, to
    /// <paramref name="outputPath"/>. The runtime's build embeds it.
    /// </summary>
    /// <returns>0 on success, 1 on failure.</returns>
    internal static int EmitEngineShader(string outputPath, ShaderTools tools, TextWriter output, TextWriter error)
    {
        string source = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath))!, "capsule-sprite.fx");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, ShaderTemplate.ComposeDefault(), Utf8NoBom);

            ShaderCompilation result = ShaderCompiler.Compile(tools, source);
            if (result.Effect is not { } effect)
            {
                error.WriteLine(result.Failure ?? string.Join('\n', result.Diagnostics));

                return 1;
            }

            AtomicFile.Write(outputPath, path => File.WriteAllBytes(path, effect));
        }
        catch (Exception ex) when (BuildPass.IsReportable(ex))
        {
            error.WriteLine($"shaders: cannot write '{outputPath}': {ex.Message}");

            return 1;
        }

        output.WriteLine($"shaders: the engine's sprite shader -> {outputPath}");

        return 0;
    }

    // The parameter table of the shipped effect, compiled again only when the composed source, this
    // compiler's build or either tool's version differs from what it was built from. The effect and
    // its table are written only once a compile succeeds, so a failed one is attempted again by the
    // next run. Null when it failed, reported.
    private static List<ShaderParameter>? CompileIfChanged(BuildPass pass, ShaderTools tools, string composed, Source source)
    {
        string compiledPath = pass.Shipped.Claim(source.Key + CompiledExtension);
        string keptSource = Path.Combine(pass.OutputDirectory, "shaders", source.Key + ".fx");
        string keptParameters = Path.ChangeExtension(keptSource, ParametersExtension);
        composed = $"// {typeof(ShaderCompiler).Assembly.ManifestModule.ModuleVersionId} {Path.GetFileName(tools.Dxc)} {Path.GetFileName(tools.SpirvCross)}\n{composed}";
        if (File.Exists(compiledPath) && File.Exists(keptParameters) && File.Exists(keptSource) && File.ReadAllText(keptSource) == composed)
        {
            return [.. File.ReadAllLines(keptParameters).Select(ReadParameter)];
        }

        Directory.CreateDirectory(Path.GetDirectoryName(keptSource)!);
        File.Delete(compiledPath);
        File.Delete(keptParameters);
        File.WriteAllText(keptSource, composed, Utf8NoBom);

        ShaderCompilation result = ShaderCompiler.Compile(tools, keptSource);

        // A warning goes to the output, where the build reports it against its line and fails nothing.
        foreach (ShaderDiagnostic diagnostic in result.Diagnostics)
        {
            (diagnostic.Warning ? pass.Output : pass.Error).WriteLine(diagnostic);
        }

        if (result.Effect is not { } effect)
        {
            if (result.Failure is { } failure)
            {
                pass.Error.WriteLine($"{source.Path}: {failure}");
            }

            return null;
        }

        File.WriteAllBytes(compiledPath, effect);
        File.WriteAllLines(keptParameters, result.Parameters.Select(static parameter => $"{parameter.Name}|{parameter.Kind}"));

        return [.. result.Parameters];
    }

    private static ShaderParameter ReadParameter(string line)
    {
        string[] fields = line.Split('|');

        return new ShaderParameter(fields[0], Enum.Parse<ShaderParameterKind>(fields[1]));
    }

    // A property with an initializer, so every read hands back the one instance materials batch by.
    private static void AppendShader(StringBuilder source, string indent, string identifier, Source shader, List<ShaderParameter> parameters)
    {
        source.Append(indent).Append("/// <summary><c>").Append(shader.Key).Append(shader.Extension).Append("</c>");
        if (parameters.Count == 0)
        {
            source.Append(", with no parameters");
        }
        else
        {
            source.Append(", setting ");
            for (int i = 0; i < parameters.Count; i++)
            {
                source.Append(i == 0 ? string.Empty : ", ").Append("<c>").Append(parameters[i].Name).Append("</c>");
            }
        }

        source.AppendLine(".</summary>");
        source.Append(indent).Append("public static ").Append(ShaderType).Append(' ').Append(identifier)
            .Append(" { get; } = new ").Append(ShaderType).Append('(').Append(Literal.Of(shader.Key));

        foreach (ShaderParameter parameter in parameters)
        {
            source.Append(", new ").Append(ParameterType).Append('(').Append(Literal.Of(parameter.Name))
                .Append(", ").Append(KindType).Append('.').Append(parameter.Kind).Append(')');
        }

        source.AppendLine(");");
    }
}
