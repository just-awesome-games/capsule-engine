using System.Globalization;

namespace Capsule.Maps.Cli;

internal static class Program
{
    private const string Usage = """
        Capsule.Maps.Cli import-tiled --out <dir> [--dependency-root <dir>] [--tile-size <px>] <map.tmj> [<map.tmj>...]
        Capsule.Maps.Cli import-tiled --out <dir> [--dependency-root <dir>] [--tile-size <px>] --maps-from <list.txt>

          Writes <dir>/<map>.map.json for every Tiled map, creating <dir> if absent. Every map
          is attempted. Exit 0 when all succeeded, 1 when any failed, 2 on a usage error.

          Name the Tiled maps by a relative path: each one is recorded verbatim in its map's
          source block, and the format rejects an absolute one.

          --tile-size is the tile size the game declares, and a map whose own differs fails.
          Omit it and no size is imposed, so a game may mix them deliberately. Capsule's build
          hook passes whatever the shell project sets CapsuleTileSize to.

          --dependency-root confines external tilesets to a tree the caller tracks. Capsule's
          build hook passes the asset-source root and includes every .tsj beneath it as an input.

          --maps-from reads the Tiled maps one per line, which is how Capsule's build hook
          (build/Capsule.Maps.targets) passes a whole project's worth of them. Running this by
          hand is for debugging.
        """;

    private static int Main(string[] args) => args switch
    {
        ["import-tiled", "--out", string outputDirectory, .. string[] rest] => ImportTiled(outputDirectory, rest),
        _ => UsageError(),
    };

    private static int ImportTiled(string outputDirectory, string[] args)
    {
        string? dependencyRoot = null;
        int? tileSize = null;
        if (args is ["--dependency-root", string root, .. string[] rest])
        {
            dependencyRoot = root;
            args = rest;
        }

        if (args is ["--tile-size", string declared, .. string[] tileSizeRest])
        {
            if (!int.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size <= 0)
            {
                return UsageError();
            }

            tileSize = size;
            args = tileSizeRest;
        }

        return args switch
        {
            ["--maps-from", string list] =>
                MapTool.ImportTiledFromList(outputDirectory, list, tileSize, Console.Out, Console.Error, dependencyRoot),
            [_, ..] => MapTool.ImportTiled(outputDirectory, args, tileSize, Console.Out, Console.Error, dependencyRoot),
            _ => UsageError(),
        };
    }

    private static int UsageError()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
