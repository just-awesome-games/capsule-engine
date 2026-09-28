using System.Text;

namespace Capsule.Build.Shaders;

/// <summary>The build's second mode: compiling the engine's own sprite shader, which the runtime's build embeds.</summary>
internal static class EngineShader
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Compiles the template around its default fragment to <paramref name="outputPath"/>.</summary>
    /// <returns>0 on success, 1 on failure.</returns>
    internal static int Emit(string outputPath, ShaderTools tools, TextWriter output, TextWriter error)
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
}
