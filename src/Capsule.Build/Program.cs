namespace Capsule.Build;

internal static class Program
{
    private const string Usage = """
        Capsule.Build --requests <requests.txt> --out <dir>

          Runs one build pass over <requests.txt>: option lines, then one
          'group|path|extension|source' request per line, the source relative to the working
          directory. Every authored path is keyed, every keyed scene is derived canonically under
          <dir>/scenes/, every keyed clip is measured into <dir>/CapsuleAssets.Audio.g.cs, every
          keyed atlas is packed under <dir>/atlases/, and the manifests the build hooks read their
          items back from are written under <dir>.

          'tile-size|<px>' is the tile size the game declares, and a scene whose grid differs fails.

          Every source is attempted. Exit 0 when all succeeded, 1 when any failed, 2 on a usage
          error. <dir>/build.stamp is written last. A failed run leaves it stale.

          'shader-dxc|<dir>' and 'shader-spirv-cross|<dir>' name the package folders of the shader
          tools the build downloaded.

          Capsule's build hooks are the only callers.

        Capsule.Build --engine-shader <out.mgfx> --dxc <dir> --spirv-cross <dir>

          Compiles the engine's own sprite shader to <out.mgfx>. The runtime's build runs it.
        """;

    private static int Main(string[] args)
    {
        if (args is ["--engine-shader", string effect, "--dxc", string dxc, "--spirv-cross", string spirvCross])
        {
            return Shaders.ShaderTool.EmitEngineShader(effect, new Shaders.ShaderTools(dxc, spirvCross), Console.Out, Console.Error);
        }

        if (args is not ["--requests", string requests, "--out", string outputDirectory])
        {
            Console.Error.WriteLine(Usage);

            return 2;
        }

        return BuildRun.Run(requests, outputDirectory, Console.Out, Console.Error);
    }
}
