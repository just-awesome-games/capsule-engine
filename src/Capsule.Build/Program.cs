namespace Capsule.Build;

internal static class Program
{
    private const string Usage = """
        Capsule.Build --requests <requests.txt> --out <dir>

          Builds a game's authoring plane: the manifest names the asset root, the options and every
          source (see BuildRequests). Writes under <dir> what the game ships in its shipped layout at
          assets/, the CapsuleAssets.g.cs it compiles, and build.stamp last. Exit 0 when every source succeeded, 1 when any failed, 2 on a usage
          error. Capsule's build targets are the only callers.

        Capsule.Build --engine-shader <out.mgfx> --dxc <dir> --spirv-cross <dir>

          Compiles the engine's own sprite shader to <out.mgfx>. The runtime's build runs it.
        """;

    private static int Main(string[] args)
    {
        if (args is ["--engine-shader", string effect, "--dxc", string dxc, "--spirv-cross", string spirvCross])
        {
            return Shaders.ShaderStep.EmitEngineShader(effect, new Shaders.ShaderTools(dxc, spirvCross), Console.Out, Console.Error);
        }

        if (args is not ["--requests", string requests, "--out", string outputDirectory])
        {
            Console.Error.WriteLine(Usage);

            return 2;
        }

        return BuildRun.Run(requests, outputDirectory, Console.Out, Console.Error);
    }
}
