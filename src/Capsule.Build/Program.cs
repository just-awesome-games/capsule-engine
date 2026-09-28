using Capsule.Build.Shaders;

namespace Capsule.Build;

internal static class Program
{
    private const string Usage = """
        Capsule.Build --requests <requests.txt> --out <dir>

          Builds a game's authoring plane: the manifest names the asset root, the options and every
          source (see BuildRequests). Writes under <dir> what the game ships in its shipped layout at
          assets/, the CapsuleAssets.g.cs it compiles, and build.stamp last. Exits 0 when the run
          reported no defect, 1 when it reported any, and 2 on a usage error. Capsule's build targets
          are the only callers.

        Capsule.Build --engine-shader <out.mgfx> --dxc <dir> --spirv-cross <dir>

          Compiles the engine's own sprite shader to <out.mgfx>. The runtime's build runs it.
        """;

    private static int Main(string[] args)
    {
        if (args is ["--engine-shader", string effect, "--dxc", string dxc, "--spirv-cross", string spirvCross])
        {
            return EngineShader.Emit(effect, new ShaderTools(dxc, spirvCross), Console.Out, Console.Error);
        }

        if (args is not ["--requests", string requests, "--out", string outputDirectory])
        {
            Console.Error.WriteLine(Usage);

            return 2;
        }

        return BuildRun.Run(requests, outputDirectory, Console.Out, Console.Error);
    }
}
