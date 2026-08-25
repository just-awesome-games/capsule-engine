namespace Capsule.Levels.Cli;

internal static class Program
{
    private const string Usage = """
        Capsule.Levels.Cli <verb>

          import-tiled <map.tmj> <out.level.json>   generate a level from a Tiled map
          assign-ids <level.json>                   number unnumbered entities, rewrite canonically
          validate <level.json> [<level.json>...]   check levels, and that generated ones match their source
        """;

    private static int Main(string[] args) => args switch
    {
        ["import-tiled", string map, string level] => LevelTool.ImportTiled(map, level, Console.Out, Console.Error),
        ["assign-ids", string level] => LevelTool.AssignIds(level, Console.Out, Console.Error),
        ["validate", .. string[] levels] when levels.Length > 0 => LevelTool.Validate(levels, Console.Out, Console.Error),
        _ => UsageError(),
    };

    private static int UsageError()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
