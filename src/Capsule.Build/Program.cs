using Capsule.Build.Shaders;

namespace Capsule.Build;

internal static class Program
{
    private const string Usage = """
        Capsule.Build --engine-shader <out.mgfx> --dxc <dir> --spirv-cross <dir>

          Compiles the engine's own sprite shader to <out.mgfx>. The runtime's build runs it. A game's
          assets build through its own build project (CapsuleBuild).
        """;

    private static int Main(string[] args)
    {
        if (args is not ["--engine-shader", string effect, "--dxc", string dxc, "--spirv-cross", string spirvCross])
        {
            Console.Error.WriteLine(Usage);

            return 2;
        }

        return EngineShader.Emit(effect, new ShaderTools(dxc, spirvCross), Console.Out, Console.Error);
    }
}
