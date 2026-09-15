namespace Capsule.Tests.Documents;

internal static class SceneDocumentFixtures
{
    /// <summary>The one authored tile-map entry every document fixture is written around.</summary>
    internal const string TileMapEntry = """
            { "id": 1, "type": "tile-map", "x": 0, "y": 0,
              "properties": { "tileSize": 16, "width": 2, "height": 1,
                              "texture": "terrain.png", "columns": 4,
                              "tileTypes": [ { "type": "empty" }, { "type": "ground", "cell": 0 } ],
                              "tiles": [0, 1] } }
        """;

    /// <summary>An authored document of <see cref="TileMapEntry"/> alone.</summary>
    internal const string AuthoredTileMap = """
        { "formatVersion": 5,
          "entities": [
        """ + TileMapEntry + """
         ],
          "nextEntityId": 2 }
        """;

    /// <summary>An authored document of <see cref="TileMapEntry"/> and one placed entity.</summary>
    internal const string AuthoredTileMapAndPlayer = """
        { "formatVersion": 5,
          "entities": [
        """ + TileMapEntry + """
        ,
            { "id": 2, "type": "player", "x": 8, "y": 0 } ],
          "nextEntityId": 3 }
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
