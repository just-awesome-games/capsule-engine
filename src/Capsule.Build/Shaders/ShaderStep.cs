using Capsule.Build.Caching;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>
/// Compiles every shader, its fragment wrapped in the engine's template, with the parameter table it
/// declares.
/// </summary>
/// <remarks>
/// Ships each at its path as <c>{key}.{format}.shader</c>, one per GPU format, and declares it as a
/// <c>Shader</c>. A misspelt shader is a compile error, and a misspelt parameter throws where the
/// material is written. A platform module's further format also gets the engine's own sprite stages,
/// which the engine commits only in its own formats.
/// </remarks>
internal static class ShaderStep
{
    private const string Step = "shaders";

    // Where each compile writes its intermediate files, a directory per shader, deleted once it has compiled.
    private const string IntermediateDirectory = "shaders";

    // Where the engine's stages ship for a further format, beside the game's shaders.
    private const string EngineStages = "capsule/";

    internal static void Run(PipelinePass pass)
    {
        ShaderTools tools = pass.Requests.ShaderTools;

        // The tools' package folder is named for its version. A further format's tool is stamped by
        // its path and its last write.
        string versions = $"shader-tools={Path.GetFileName(tools.Package)}; container={ShaderContainer.Version}"
            + string.Concat(tools.Further.Select(static further => $"; {further.Format}={further.Tool}@{File.GetLastWriteTimeUtc(further.Tool).Ticks}"));

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

        if (tools.Further.Count > 0)
        {
            pass.Each(
                Step,
                [EngineStages],
                stages => new Derivation(stages, [], versions, typeof(ShaderStep).Assembly),
                (stages, files) => CompileEngineStages(pass, tools, files));
        }
    }

    // Composes and compiles one shader and ships a container per format. A compile that failed reports
    // its errors and warnings here, and the pass leaves it out.
    private static ShaderFacts Compile(PipelinePass pass, ShaderTools tools, Source source, DerivedFiles files)
    {
        string basePath = Path.Combine(pass.OutputDirectory, IntermediateDirectory, source.Key, Path.GetFileName(source.Key));
        ShaderCompilation result = ShaderCompiler.CompileFragment(tools, basePath, File.ReadAllText(source.Path), source.Path);

        if (result.Shaders is not { } shaders)
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

        Ship(files, source.Key, shaders);

        return new ShaderFacts([.. result.Parameters], [.. result.Diagnostics.Where(static diagnostic => diagnostic.Warning)]);
    }

    // The engine's sprite fragment and vertex stage in each further format, which a host on that
    // format reads in place of its embedded ones.
    private static void CompileEngineStages(PipelinePass pass, ShaderTools tools, DerivedFiles files)
    {
        string directory = Path.Combine(pass.OutputDirectory, IntermediateDirectory, EngineStages);
        ShaderCompilation sprite = ShaderCompiler.CompileFragment(tools, Path.Combine(directory, "sprite", "sprite"), ShaderTemplate.DefaultFragment, ShaderTemplate.DefaultFragmentFile);
        ShaderCompilation vertex = ShaderCompiler.CompileVertex(tools, Path.Combine(directory, "vertex", "vertex"));
        if (sprite.Shaders is not { } fragments || vertex.Shaders is not { } vertices)
        {
            pass.Fail(ShaderTemplate.TemplateFile, $"the engine's sprite shader did not compile to a further format: {sprite.Failure ?? vertex.Failure ?? string.Join("; ", sprite.Diagnostics.Concat(vertex.Diagnostics))}");
            return;
        }

        HashSet<string> further = [.. tools.Further.Select(static tool => tool.Format)];
        Ship(files, EngineStages + "sprite", [.. fragments.Where(shader => further.Contains(shader.Format))]);
        Ship(files, EngineStages + "vertex", [.. vertices.Where(shader => further.Contains(shader.Format))]);
    }

    private static void Ship(DerivedFiles files, string key, IEnumerable<ShaderContainer> shaders)
    {
        foreach (ShaderContainer shader in shaders)
        {
            files.Write(key + ShaderContainer.Suffix(shader.Format), path => File.WriteAllBytes(path, shader.Write()));
        }
    }
}
