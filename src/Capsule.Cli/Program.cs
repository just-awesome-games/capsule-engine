using System.Globalization;

namespace Capsule.Cli;

internal static class Program
{
    private const string Usage = """
        Capsule.Cli import-tiled --out <dir> [--dependency-root <dir>] [--tile-size <px>] <scene.tmj> [<scene.tmj>...]
        Capsule.Cli import-tiled --out <dir> [--dependency-root <dir>] [--tile-size <px>] --scenes-from <list.txt>
        Capsule.Cli import-native --out <dir> [--tile-size <px>] <scene.scene.json> [<scene.scene.json>...]
        Capsule.Cli import-native --out <dir> [--tile-size <px>] --scenes-from <list.txt>

          Writes <dir>/<scene>.scene.json for every source, creating <dir> if absent. Every source
          is attempted. Exit 0 when all succeeded, 1 when any failed, 2 on a usage error.

          import-tiled reads Tiled maps; import-native reads scene documents already in Capsule's
          own format, validates them and re-emits them canonically, so nothing ships unvalidated
          and nothing ships uncanonicalised.

          Name the sources by a relative path: each one is recorded verbatim in its document's
          source block, and the format rejects an absolute one.

          --tile-size is the tile size the game declares, and a scene whose grid differs fails.
          Omit it and no size is imposed, so a game may mix them deliberately. Capsule's build
          hook passes whatever the shell project sets CapsuleTileSize to.

          --dependency-root confines external tilesets to a tree the caller tracks. Capsule's
          build hook passes the asset-source root and includes every .tsj beneath it as an input.

          --scenes-from reads the sources one per line, which is how Capsule's build hook
          (build/Capsule.SceneDocuments.targets) passes a whole project's worth of them. Running
          this by hand is for debugging.
        """;

    private static int Main(string[] args) => args switch
    {
        ["import-tiled", "--out", string outputDirectory, .. string[] rest] => ImportTiled(outputDirectory, rest),
        ["import-native", "--out", string outputDirectory, .. string[] rest] => ImportNative(outputDirectory, rest),
        _ => UsageError(),
    };

    private static int ImportTiled(string outputDirectory, string[] args)
    {
        string? dependencyRoot = null;
        if (args is ["--dependency-root", string root, .. string[] rest])
        {
            dependencyRoot = root;
            args = rest;
        }

        if (!TryTakeTileSize(ref args, out int? tileSize))
        {
            return UsageError();
        }

        return args switch
        {
            ["--scenes-from", string list] =>
                SceneDocumentTool.ImportTiledFromList(outputDirectory, list, tileSize, Console.Out, Console.Error, dependencyRoot),
            [_, ..] => SceneDocumentTool.ImportTiled(outputDirectory, args, tileSize, Console.Out, Console.Error, dependencyRoot),
            _ => UsageError(),
        };
    }

    private static int ImportNative(string outputDirectory, string[] args)
    {
        if (!TryTakeTileSize(ref args, out int? tileSize))
        {
            return UsageError();
        }

        return args switch
        {
            ["--scenes-from", string list] =>
                SceneDocumentTool.ImportNativeFromList(outputDirectory, list, tileSize, Console.Out, Console.Error),
            [_, ..] => SceneDocumentTool.ImportNative(outputDirectory, args, tileSize, Console.Out, Console.Error),
            _ => UsageError(),
        };
    }

    private static bool TryTakeTileSize(ref string[] args, out int? tileSize)
    {
        tileSize = null;
        if (args is not ["--tile-size", string declared, .. string[] rest])
        {
            return true;
        }

        if (!int.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size <= 0)
        {
            return false;
        }

        tileSize = size;
        args = rest;

        return true;
    }

    private static int UsageError()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
