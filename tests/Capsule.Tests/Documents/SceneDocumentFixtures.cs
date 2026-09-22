using Capsule.Assets;
using Capsule.Scenes.Documents;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

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
        { "formatVersion": 6,
          "entities": [
        """ + TileMapEntry + """
         ],
          "nextEntityId": 2 }
        """;

    /// <summary>An authored document of <see cref="TileMapEntry"/> and one placed entity.</summary>
    internal const string AuthoredTileMapAndPlayer = """
        { "formatVersion": 6,
          "entities": [
        """ + TileMapEntry + """
        ,
            { "id": 2, "type": "player", "x": 8, "y": 0 } ],
          "nextEntityId": 3 }
        """;

    internal const string Sha256 = "c304030d3d53c9c440cd5d251080080a16b34be3832ad1218b2b63cae622cf6d";

    internal const string Coin = """
        ,
            {
              "id": 2,
              "type": "coin",
              "x": 8,
              "y": 0
            }
        """;

    internal static readonly TextureHandle Atlas = SceneFixtures.TerrainAtlas;

    // A tile-map entry with no properties, and the least grid that parses, which the defect theory
    // edits one field of per case.
    internal const string TileMapWithoutProperties =
        """{"formatVersion": 6, "entities": [{"id": 1, "type": "tile-map", "x": 0, "y": 0}], "nextEntityId": 2}""";

    internal const string Grid1x1 =
        """
        {"formatVersion": 6, "entities": [{"id": 1, "type": "tile-map", "x": 0, "y": 0,
          "properties": {"tileSize": 16, "width": 1, "height": 1,
                         "tileTypes": [{"type": "empty"}], "tiles": [0]}}], "nextEntityId": 2}
        """;

    internal static SceneDocument Drawing(TextureHandle texture) =>
        new([new TileMapPlacement(1, new TileGrid(16, 2, 1, [TileGrid.EmptyTile, SceneFixtures.Tile("ground", 0)], [0, 1], texture, 4))], 2);

    internal static TileMapPlacement Terrain() =>
        new(1, new TileGrid(16, 2, 1, [TileGrid.EmptyTile, SceneFixtures.Tile("ground", 0)], [0, 1], Atlas, 4));

    internal static TileMapPlacement TileMapOf(SceneDocument document, int index = 0) =>
        document.Entries[index].TileMap!.Value;

    internal static string Palette(int cell) =>
        $$"""[{"type": "empty"}, {"type": "ground", "cell": {{cell}}}]""";

    internal static string DocumentText(
        string? tileTypes = null,
        string tiles = "[0, 1]",
        string entities = "",
        int nextEntityId = 2,
        string extra = "",
        string texture = "\"terrain.png\"",
        string scale = "") =>
        $$"""
        {
          "formatVersion": 6,
          "entities": [
            {
              "id": 1,
              "type": "tile-map",
              "x": 0,
              "y": 0,
              {{scale}}
              "properties": {
                "tileSize": 16,
                "width": 2,
                "height": 1,
                "texture": {{texture}},
                "columns": 4,
                "tileTypes": {{tileTypes ?? Palette(0)}},
                "tiles": {{tiles}}
              }
            }{{entities}}
          ],
          "nextEntityId": {{nextEntityId}}{{extra}}
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
