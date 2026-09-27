using Capsule.Build.Scenes;
using Capsule.Scenes.Documents;

namespace Capsule.Tests.Documents;

public sealed class EntryPropertiesFormatTests
{
    // A game entry's properties are its class's to read, so the importer keeps them as authored.
    // A property named like a grid or palette field sits ahead of the tile map and stays as written.
    [Fact]
    public void AGameEntrysProperties_AreAFixedPointOfTheImporter_BesideATileMap()
    {
        string json = """
            {
              "formatVersion": 7,
              "entities": [
                {
                  "id": 1,
                  "type": "lift",
                  "x": 8,
                  "y": 0,
                  "properties": {
                    "rise": 96.5,
                    "tiles": [
                      1,
                      2
                    ],
                    "shape": [
                      "["
                    ],
                    "label": null
                  }
                },
                {
                  "id": 2,
                  "type": "tile-map",
                  "x": 0,
                  "y": 0,
                  "properties": {
                    "tileSize": 16,
                    "width": 2,
                    "height": 1,
                    "tileTypes": [
                      {
                        "type": "empty"
                      }
                    ],
                    "tiles": [
                      0, 0
                    ]
                  }
                }
              ],
              "nextEntityId": 3
            }

            """.ReplaceLineEndings("\n");

        Assert.Equal(json, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(json)));
    }

    // The build writes each game entry as an attribute the compiler checks, every JSON value as the C#
    // constant of its kind, and where the entry starts in the file it read.
    [Fact]
    public void AGameEntry_ReachesTheGeneratorAsCSharpConstants()
    {
        string json = """
            {"formatVersion": 7, "entities": [
              {"id": 1, "type": "coin", "x": 0, "y": 0},
              {"id": 2, "type": "lift", "x": 0, "y": 0, "properties": {
                "on": true, "ticks": 40, "rise": 96.5, "far": 3000000000, "label": "a\"b", "none": null,
                "size": [2, 2.5], "route": {"to": 1}}}
            ], "nextEntityId": 3}
            """;

        Assert.Equal(
            [
                "CapsuleGeneratedSceneDocument(Path = \"Assets/Scenes/room.scene.json\")",
                "CapsuleGeneratedPlacement(1, \"coin\", Line = 2, Column = 3)",
                "CapsuleGeneratedPlacement(2, \"lift\", \"on\", true, \"ticks\", 40, \"rise\", 96.5D, \"far\", 3000000000D, "
                    + "\"label\", \"a\\\"b\", \"none\", null, \"size\", new object[] { 2, 2.5D }, \"route\", typeof(object), Line = 3, Column = 3)",
            ],
            SceneStep.Attributes(SceneDocumentFile.Parse(json), "Assets/Scenes/room.scene.json", json));
    }
}
