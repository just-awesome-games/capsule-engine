namespace Capsule.Tests.Documents;

internal static class SceneDocumentFixtures
{
    /// <summary>An authored document of a tile-map entry and one placed entity, which a build ships as it reads it.</summary>
    internal const string AuthoredTileMapAndPlayer = """
        { "entities": [
            { "id": 1, "type": "tile-map",
              "tileSize": 16, "width": 2, "height": 1,
              "texture": "terrain.png", "columns": 4,
              "tileTypes": [ { "name": "empty" }, { "name": "ground", "cell": 0 } ],
              "tiles": [0, 1] },
            { "id": 2, "type": "player", "x": 8, "y": 0 } ] }
        """;

    internal const string Coin = """
        ,
            {
              "id": 2,
              "type": "coin",
              "x": 8,
              "y": 0
            }
        """;

    /// <summary>A document of one coin with id 1, then <paramref name="entities"/>.</summary>
    internal static string DocumentText(string entities = "") =>
        $$"""
        {
          "entities": [
            {
              "id": 1,
              "type": "coin"
            }{{entities}}
          ]
        }
        """;

    internal sealed class Workspace : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("capsule-scenes-");
        private readonly string _entryDirectory = Directory.GetCurrentDirectory();

        internal Workspace() => Directory.SetCurrentDirectory(_directory.FullName);

        internal string Write(string name, string text)
        {
            string path = Path.Combine(_directory.FullName, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);

            return name;
        }

        // Out of the tree before deleting it: a working directory cannot be removed on Windows.
        public void Dispose()
        {
            Directory.SetCurrentDirectory(_entryDirectory);
            _directory.Delete(recursive: true);
        }
    }
}
