namespace Capsule.Levels.Cli;

internal static class Program
{
    private const string Usage = """
        Capsule.Levels.Cli import-tiled --out <dir> <map.tmj> [<map.tmj>...]
        Capsule.Levels.Cli import-tiled --out <dir> --maps-from <list.txt>

          Writes <dir>/<map>.level.json for every map, creating <dir> if absent. Every map is
          attempted. Exit 0 when all succeeded, 1 when any failed, 2 on a usage error.

          Name the maps by a relative path: each one is recorded verbatim in its level's
          source block, and the format rejects an absolute one.

          --maps-from reads the maps one per line, which is how Capsule's build hook
          (build/Capsule.Levels.targets) passes a whole project's worth of them. Running this
          by hand is for debugging.
        """;

    private static int Main(string[] args) => args switch
    {
        ["import-tiled", "--out", string outputDirectory, "--maps-from", string list] =>
            LevelTool.ImportTiledFromList(outputDirectory, list, Console.Out, Console.Error),
        ["import-tiled", "--out", string outputDirectory, .. string[] maps] when maps.Length > 0 =>
            LevelTool.ImportTiled(outputDirectory, maps, Console.Out, Console.Error),
        _ => UsageError(),
    };

    private static int UsageError()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
