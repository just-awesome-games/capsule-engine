using System.Globalization;

namespace Capsule.Build;

internal static class Program
{
    private const string Usage = """
        Capsule.Build --out <dir> [--tile-size <px>] --scenes-from <list.txt>
        Capsule.Build --out <dir> --sheets-from <list.txt> --textures <list.txt> --generated <file.cs>

          Validates every document named in <list.txt> — one 'key|path' per line, the path relative
          to the working directory — and writes it canonically at <dir>/<key>. A line with no key is
          keyed by its file name without extensions. Every source is attempted; exit 0 when all
          succeeded, 1 when any failed, 2 on a usage error.

          --tile-size is the tile size the game declares, and a scene whose grid differs fails.
          --textures names the game's textures by their path under the textures root, one per line,
          and a sheet cutting from anything else fails. --generated is the C# file the whole sheet
          set is rendered as.

          Capsule's build hooks are the only callers.
        """;

    private static int Main(string[] args)
    {
        if (args is not ["--out", string outputDirectory, .. string[] rest])
        {
            return UsageError();
        }

        if (rest is ["--sheets-from", string sheetList, "--textures", string textureList, "--generated", string generated])
        {
            return SpriteSheetTool.ImportFromList(
                outputDirectory, sheetList, textureList, generated, Console.Out, Console.Error);
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
