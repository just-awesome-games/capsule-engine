using System.Globalization;

namespace Capsule.Build;

internal static class Program
{
    private const string Usage = """
        Capsule.Build --out <dir> [--tile-size <px>] --scenes-from <list.txt>

          Validates every scene document named in <list.txt> (one path per line, relative to the
          working directory) and writes it canonically as <dir>/<scene>.scene.json, creating <dir>
          if absent. Every source is attempted. Exit 0 when all succeeded, 1 when any failed, 2 on
          a usage error.

          --tile-size is the tile size the game declares, and a scene whose grid differs fails.
          Omit it and no size is imposed.

          Capsule's build hook (build/Capsule.SceneDocuments.targets) is the only caller.
        """;

    private static int Main(string[] args)
    {
        if (args is not ["--out", string outputDirectory, .. string[] rest])
        {
            return UsageError();
        }

        int? tileSize = null;
        if (rest is ["--tile-size", string declared, .. string[] afterTileSize])
        {
            if (!int.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out int size) || size <= 0)
            {
                return UsageError();
            }

            tileSize = size;
            rest = afterTileSize;
        }

        return rest is ["--scenes-from", string list]
            ? SceneDocumentTool.ImportFromList(outputDirectory, list, tileSize, Console.Out, Console.Error)
            : UsageError();
    }

    private static int UsageError()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
