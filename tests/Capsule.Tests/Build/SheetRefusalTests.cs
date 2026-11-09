namespace Capsule.Tests.Build;

/// <summary>
/// The sheets the build refuses to read, each failing the build with a message that names the file and
/// the defect.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class SheetRefusalTests
{
    [Theory]

    // The version gate.
    [InlineData("""{ "texture": "p.png", "frames": [] }""", "formatVersion")]
    [InlineData("""{ "formatVersion": 2, "texture": "p.png", "frames": [] }""", "unsupported")]

    // A texture that is not one asset path under the textures root.
    [InlineData("""{ "formatVersion": 1, "frames": [] }""", "names no texture")]
    [InlineData("""{ "formatVersion": 1, "texture": null, "frames": [] }""", "names no texture")]
    [InlineData("""{ "formatVersion": 1, "texture": "sub//p.png", "frames": [] }""", "extension included")]
    [InlineData("""{ "formatVersion": 1, "texture": "../p.png", "frames": [] }""", "extension included")]
    [InlineData("""{ "formatVersion": 1, "texture": "player", "frames": [] }""", "extension included")]
    [InlineData("""{ "formatVersion": 1, "texture": "01/p.png", "frames": [] }""", "no C# name")]

    // A sheet names at least one region of its texture.
    [InlineData("""{ "formatVersion": 1, "texture": "p.png" }""", "has no frames")]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", "frames": [] }""", "has no frames")]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", "frames": [ null ] }""", "frames[0] as null")]

    // Frame geometry.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 0, "height": 1 } ] }
        """, "at least one texel")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "flipX": true } ] }
        """, "with no x")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": -1, "y": 0, "width": 1, "height": 1 } ] }
        """, "not negative")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "y": 0, "width": 1, "height": 1 } ] }
        """, "no x")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": null, "y": 0, "width": 1, "height": 1 } ] }
        """, "no x")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": [1] } ] }
        """, "1 components")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": [1, 2, 3] } ] }
        """, "3 components")]

    // Names.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 },
                      { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "second \"a\"")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a-b", "x": 0, "y": 0, "width": 1, "height": 1 },
                      { "name": "a_b", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "one C# name")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a b", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "no C# name")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "frames", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "'Frames' class")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "clips", "frames": [ { "frame": "a", "ticks": 1 } ] } ] }
        """, "'Clips' class")]

    // Sockets, boxes and events: declared once, set by name, and set by at least one frame or entry.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "boxes": [ { "name": "hurt" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "boxes": { "hrt": { "x": 0, "y": 0, "width": 1, "height": 1 } } } ] }
        """, "setting box \"hrt\", which the sheet does not declare")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "events": [ { "name": "step" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a", "ticks": 1, "events": ["step", "stpe"] } ] } ] }
        """, "raising event \"stpe\", which the sheet does not declare")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzle": [1, 1] } } ] }
        """, "does not declare")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" }, { "name": "hand" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzzle": [1, 1] } } ] }
        """, "no frame sets")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" }, { "name": "muzzle" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzzle": [1, 1] } } ] }
        """, "second \"muzzle\"")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "sockets" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "sockets": [1, 1] } } ] }
        """, "'Sockets' class")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzzle": [1] } } ] }
        """, "1 components")]

    // A JSON number too large for a float reads as an infinity, which no texel offset is.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": [1e400, 0] } ] }
        """, "pivot that is not finite")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "sockets": [ { "name": "muzzle" } ],
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "sockets": { "muzzle": [1e400, 1] } } ] }
        """, "not finite")]

    // Clip entries.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "b", "ticks": 1 } ] } ] }
        """, "no frame named")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a", "ticks": 0 } ] } ] }
        """, "at least one fixed step")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [] } ] }
        """, "no frames")]

    // "$schema" is a string at the root and nowhere else.
    [InlineData("""{ "$schema": null, "formatVersion": 1, "texture": "p.png", "frames": [] }""", "\"$schema\" is null")]
    [InlineData("""{ "$schema": {}, "formatVersion": 1, "texture": "p.png", "frames": [] }""", "\"$schema\" is an object")]
    [InlineData("""
        { "$schema": "sheet.schema.json", "formatVersion": 1, "texture": "p.png",
          "frames": [ { "$schema": "sheet.schema.json", "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """, "has an unknown member at $.frames[0].$schema (line 2, byte 27). A sheet holds")]
    public void ASheetTheBuildCannotRead_FailsTheBuildNamingTheDefect(string json, string defect)
    {
        string refused = SheetToolTests.Refused(("prop", json));

        Assert.Contains("Assets/Sprites/prop.sheet.json", refused, StringComparison.Ordinal);
        Assert.Contains(defect, refused, StringComparison.Ordinal);
    }

    // JSON the reader cannot get through at all, or a member the format does not declare, which is
    // what a typo looks like. The reader's own message names the member and the line.
    [Theory]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "fps": 12,
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ] }
        """)]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "w": 1, "height": 1 } ] }
        """)]
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", """)]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 01, "y": 0, "width": 1, "height": 1 } ] }
        """)]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1, "pivot": [.5, 0] } ] }
        """)]

    public void JsonTheReaderRefuses_FailsTheBuild(string json) =>
        Assert.Contains("Assets/Sprites/prop.sheet.json", SheetToolTests.Refused(("prop", json)), StringComparison.Ordinal);
}
