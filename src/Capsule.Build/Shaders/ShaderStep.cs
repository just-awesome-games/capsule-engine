using Capsule.Build.Caching;

namespace Capsule.Build.Shaders;

/// <summary>
/// Compiles every shader, its fragment wrapped in the engine's template, with the parameter table it
/// declares. Ships each at its path as <c>.mgfx</c> and declares it as a <c>Shader</c>. A misspelt
/// shader is a compile error, and a misspelt parameter throws where the material is written.
/// </summary>
internal static class ShaderStep
{
    private const string Step = "shaders";

    private const string CompiledExtension = ".mgfx";

    // Where each composed source is written for the compiler, and deleted once it has compiled.
    private const string ComposedDirectory = "shaders";

    internal static void Run(PipelinePass pass)
    {
        ShaderTools tools = pass.Requests.ShaderTools;

        // Each tool's package folder is named for its version.
        string versions = $"dxc={Path.GetFileName(tools.Dxc)}; spirv-cross={Path.GetFileName(tools.SpirvCross)}";
        foreach ((Source shader, ShaderFacts compiled) in pass.Each(
            Step,
            pass.Of(AssetType.Shaders),
            source => Derivation.Of(source, versions),
            (source, files) => Compile(pass, tools, source, files),
            DerivationCacheJsonContext.Default.ShaderFacts))
        {
            foreach (ShaderDiagnostic warning in compiled.Warnings)
            {
                pass.Warn(warning.Anchor, warning.Report);
            }

            pass.Declare(shader, compiled.Parameters, ShaderMembers.Write);
        }
    }

    // Composes and compiles one shader and ships its effect. A compile that failed reports its errors
    // and warnings here, and the pass leaves it out.
    private static ShaderFacts Compile(PipelinePass pass, ShaderTools tools, Source source, DerivedFiles files)
    {
        string composed = Path.Combine(pass.OutputDirectory, ComposedDirectory, source.Key + ".fx");
        Directory.CreateDirectory(Path.GetDirectoryName(composed)!);
        File.WriteAllText(composed, ShaderTemplate.Compose(File.ReadAllText(source.Path), source.Path));

        ShaderCompilation result;
        try
        {
            result = ShaderCompiler.Compile(tools, composed);
        }
        finally
        {
            File.Delete(composed);
        }

        if (result.Effect is not { } effect)
        {
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

            if (result.Failure is { } failure)
            {
                pass.Fail(source.Path, failure);
            }

            return new ShaderFacts([], []);
        }

        files.Write(source.Key + CompiledExtension, path => File.WriteAllBytes(path, effect));

        return new ShaderFacts([.. result.Parameters], [.. result.Diagnostics.Where(static diagnostic => diagnostic.Warning)]);
    }
}
