using System.Text;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>
/// The shader half of the build hook. Wraps every authored fragment in the engine's template, compiles
/// it with the parameter table it declares, and renders the set as a single C# file a game compiles.
/// The compiled effects ship under <c>assets/shaders/</c>.
/// </summary>
internal static class ShaderTool
{
    private const string Name = "shaders";

    private const string CompiledExtension = ".mgfx";

    // Beside each compiled effect: the parameter table it was compiled with, one 'name|kind' per line,
    // which an unchanged source reads back instead of compiling again.
    private const string ParametersExtension = ".parameters";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Compiles every source into <paramref name="shadersDirectory"/> and renders the set at
    /// <paramref name="generatedPath"/>. A source whose composed text matches the one its compiled
    /// effect was built from is not compiled again.
    /// </summary>
    /// <param name="sources">The fragments to compile, each with the key it claims.</param>
    /// <param name="tools">The shader tools the build downloaded, or null when none were named.</param>
    /// <param name="shadersDirectory">Where composed sources and compiled effects are kept between builds.</param>
    /// <param name="generatedPath">Where the generated C# is written.</param>
    /// <param name="shipped">Receives one shipped-asset line per compiled effect.</param>
    /// <param name="output">Progress, one line per source, and every compiler warning.</param>
    /// <param name="error">Failures, each anchored to the source and line that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    internal static int Emit(
        IReadOnlyList<DocumentSource> sources,
        ShaderTools? tools,
        string shadersDirectory,
        string generatedPath,
        List<string> shipped,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(shipped);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (tools is not { } downloaded)
        {
            error.WriteLine(
                $"{sources[0].Path}: the build named no shader tools. Restore the project, which downloads them for a game with shaders.");

            return 1;
        }

        List<CompiledShader> compiled = new(sources.Count);
        Dictionary<string, string> sourceOf = new(StringComparer.Ordinal);
        int failures = 0;

        foreach (DocumentSource source in sources)
        {
            string compiledPath = Path.Combine(shadersDirectory, source.Key + CompiledExtension);

            try
            {
                string composed = ShaderTemplate.Compose(File.ReadAllText(source.Path), source.Path);
                if (CompileIfChanged(downloaded, composed, compiledPath, source, output, error) is not { } parameters)
                {
                    failures++;
                    continue;
                }

                compiled.Add(new CompiledShader(source.Key, parameters));
                sourceOf[source.Key] = source.Path;
                shipped.Add($"assets/{Name}/{source.Key}{CompiledExtension}{BuildRequests.Separator}{Path.GetFullPath(compiledPath).Replace('\\', '/')}");
                output.WriteLine($"{Name}: {source.Path} -> {source.Key}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{source.Path}: {ex.Message}");
                failures++;
            }
        }

        string? generated = null;
        if (failures == 0)
        {
            generated = ShaderRegistrySource.Render(compiled, out string? refused, out string? because);
            if (generated is null)
            {
                error.WriteLine($"{sourceOf[refused!]}: {because}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"{Name}: {failures} of {sources.Count} source(s) failed");

            return 1;
        }

        try
        {
            AtomicFile.Write(generatedPath, path => File.WriteAllText(path, generated!, Utf8NoBom));
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot write '{generatedPath}': {ex.Message}");

            return 1;
        }

        output.WriteLine($"{Name}: {compiled.Count} shader(s) -> {generatedPath}");

        return 0;
    }

    /// <summary>
    /// Compiles the engine's own sprite shader, the template around its default fragment, to
    /// <paramref name="outputPath"/>. The runtime's build embeds it.
    /// </summary>
    /// <returns>0 on success, 1 on failure.</returns>
    internal static int EmitEngineShader(string outputPath, ShaderTools tools, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

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
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot write '{outputPath}': {ex.Message}");

            return 1;
        }

        output.WriteLine($"{Name}: the engine's sprite shader -> {outputPath}");

        return 0;
    }

    // The parameter table of the effect beside compiledPath, compiled again only when the composed
    // source, this compiler's build or either tool's version differs from what it was built from. The effect and its table are written only once a
    // compile succeeds, so a failed one is attempted again by the next run. Null when it failed,
    // reported.
    private static List<ShaderParameter>? CompileIfChanged(
        ShaderTools tools,
        string composed,
        string compiledPath,
        DocumentSource source,
        TextWriter output,
        TextWriter error)
    {
        string keptSource = Path.ChangeExtension(compiledPath, ".fx");
        string keptParameters = Path.ChangeExtension(compiledPath, ParametersExtension);
        composed = $"// {typeof(ShaderCompiler).Assembly.ManifestModule.ModuleVersionId} {Path.GetFileName(tools.Dxc)} {Path.GetFileName(tools.SpirvCross)}\n{composed}";
        if (File.Exists(compiledPath) && File.Exists(keptParameters) && File.Exists(keptSource) && File.ReadAllText(keptSource) == composed)
        {
            return [.. File.ReadAllLines(keptParameters).Select(ReadParameter)];
        }

        Directory.CreateDirectory(Path.GetDirectoryName(compiledPath)!);
        File.Delete(compiledPath);
        File.Delete(keptParameters);
        File.WriteAllText(keptSource, composed, Utf8NoBom);

        ShaderCompilation result = ShaderCompiler.Compile(tools, keptSource);

        // A warning goes to the output, where the build reports it against its line and fails nothing.
        foreach (ShaderDiagnostic diagnostic in result.Diagnostics)
        {
            (diagnostic.Warning ? output : error).WriteLine(diagnostic);
        }

        if (result.Effect is not { } effect)
        {
            if (result.Failure is { } failure)
            {
                error.WriteLine($"{source.Path}: {failure}");
            }

            return null;
        }

        File.WriteAllBytes(compiledPath, effect);
        File.WriteAllLines(keptParameters, result.Parameters.Select(static parameter => $"{parameter.Name}{BuildRequests.Separator}{parameter.Kind}"));

        return [.. result.Parameters];
    }

    private static ShaderParameter ReadParameter(string line)
    {
        string[] fields = line.Split(BuildRequests.Separator);

        return new ShaderParameter(fields[0], Enum.Parse<ShaderParameterKind>(fields[1]));
    }

    private static bool IsReportable(Exception exception) =>
        exception is FormatException or IOException or UnauthorizedAccessException;
}
