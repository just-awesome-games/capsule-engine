using System.Text;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>
/// Compiles every shader, its fragment wrapped in the engine's template, with the parameter table it
/// declares. Ships each at its path as <c>.mgfx</c> and declares it as a <c>Shader</c>. A misspelt
/// shader is a compile error, and a misspelt parameter throws where the material is written.
/// </summary>
internal static class ShaderStep
{
    private const string CompiledExtension = ".mgfx";

    // Beside each composed source it was compiled from, not shipped: the parameter table, one
    // 'name|kind' per line, which an unchanged source reads back instead of compiling again.
    private const string ParametersExtension = ".parameters";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    internal static void Run(BuildPass pass)
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
                // A compile that failed reported its errors, and the pass leaves it out.
                if (CompileIfChanged(pass, tools, composed, source) is not { } parameters)
                {
                    return [];
                }

                pass.Progress("shaders", source);

                return parameters;
            }))
        {
            pass.Declare(shader, parameters, ShaderMembers.Write);
        }
    }

    // The parameter table of the shipped effect, compiled again only when the composed source, this
    // compiler's build or either tool's version differs from what it was built from. The effect and
    // its table are written only once a compile succeeds, so a failed one is attempted again by the
    // next run. Null when it failed, reported.
    private static List<ShaderParameter>? CompileIfChanged(BuildPass pass, ShaderTools tools, string composed, Source source)
    {
        string compiledPath = pass.Shipped.Claim(source.Key + CompiledExtension, $"'{source.Path}'");
        string keptSource = Path.Combine(pass.CacheDirectory("shaders"), source.Key + ".fx");
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

        // Each is reported against the line the compiler anchored it to.
        foreach (ShaderDiagnostic diagnostic in result.Diagnostics)
        {
            if (diagnostic.Warning)
            {
                pass.Warn(diagnostic.Anchor, diagnostic.Report);
            }
            else
            {
                pass.Fail(diagnostic.Anchor, diagnostic.Report);
            }
        }

        if (result.Effect is not { } effect)
        {
            if (result.Failure is { } failure)
            {
                pass.Fail(source.Path, failure);
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
}
