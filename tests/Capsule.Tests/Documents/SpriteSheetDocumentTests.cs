using System.Numerics;
using Capsule.Build.Sprites;

namespace Capsule.Tests.Documents;

public sealed class SpriteSheetDocumentTests
{
    private const string Authored = """
        { "formatVersion": 1,
          "texture": "player.png",
          "frames": [
            { "name": "idle-0", "x": 0, "y": 0, "width": 8, "height": 8, "pivot": [4, 8] },
            { "name": "walk-0", "x": 8, "y": 0, "width": 8, "height": 8 } ],
          "clips": [
            { "name": "idle", "loop": true, "frames": [ { "frame": "idle-0", "ticks": 6 } ] },
            { "name": "land", "frames": [ { "frame": "walk-0", "ticks": 1 } ] } ] }
        """;

    [Fact]
    public void ParseReadsTheTextureTheFramesAndTheClips()
    {
        SpriteSheetDocument document = SpriteSheetDocumentFile.Parse(Authored);

        Assert.Equal("player", document.Texture.Name);
        Assert.Equal(".png", document.Texture.Extension);
        Assert.Equal(new Vector2(4, 8), document.Frames[0].Pivot);
        Assert.Equal(8, document.Frames[1].Region.X);

        // An absent pivot is the region's top-left corner, which is Sprite.Pivot's own default.
        Assert.Equal(Vector2.Zero, document.Frames[1].Pivot);
        Assert.True(document.Clips[0].Loop);
        Assert.False(document.Clips[1].Loop);
        Assert.Equal(6, document.Clips[0].Frames[0].Ticks);
        Assert.Null(document.Source);
    }

    [Fact]
    public void TheCanonicalFormIsAFixedPointOfTheImporter()
    {
        string canonical = SpriteSheetDocumentFile.ToJson(SpriteSheetDocumentFile.Parse(Authored));

        Assert.Equal(canonical, SpriteSheetDocumentFile.ToJson(SpriteSheetDocumentFile.Parse(canonical)));
        Assert.EndsWith("}\n", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', canonical);

        // Neither default is written back: a top-left pivot and a clip played once say nothing.
        Assert.DoesNotContain("\"loop\": false", canonical, StringComparison.Ordinal);
        Assert.Equal(1, Count(canonical, "\"pivot\""));
    }

    // An authoring module's document arrives stamped with the file a person edited, and that is the
    // provenance the derived document must keep.
    [Fact]
    public void ASourceBlockSurvivesTheRoundTrip()
    {
        SpriteSheetDocument document = SpriteSheetDocumentFile.Parse(Authored) with
        {
            Source = new SpriteSheetSource("aseprite", "../asset-sources/sprites/player.aseprite", new string('a', 64)),
        };

        SpriteSheetDocument read = SpriteSheetDocumentFile.Parse(SpriteSheetDocumentFile.ToJson(document));

        Assert.Equal("aseprite", read.Source?.Tool);
        Assert.Equal("../asset-sources/sprites/player.aseprite", read.Source?.Path);
    }

    [Theory]
    // The version gate.
    [InlineData("""{ "texture": "p.png", "frames": [], "clips": [] }""", "formatVersion")]
    [InlineData("""{ "formatVersion": 2, "texture": "p.png", "frames": [], "clips": [] }""", "unsupported")]
    // A texture that is not one flat file name.
    [InlineData("""{ "formatVersion": 1, "frames": [], "clips": [] }""", "names no texture")]
    [InlineData("""{ "formatVersion": 1, "texture": "sub/p.png", "frames": [], "clips": [] }""", "no path segments")]
    [InlineData("""{ "formatVersion": 1, "texture": "player", "frames": [], "clips": [] }""", "no path segments")]
    // Empty lists.
    [InlineData("""{ "formatVersion": 1, "texture": "p.png", "frames": [], "clips": [] }""", "empty frames list")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ], "clips": [] }
        """, "empty clips list")]
    // Frame geometry.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 0, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a", "ticks": 1 } ] } ] }
        """, "at least one texel")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": -1, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a", "ticks": 1 } ] } ] }
        """, "not negative")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a", "ticks": 1 } ] } ] }
        """, "has no x")]
    // Names.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 },
                      { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a", "ticks": 1 } ] } ] }
        """, "second \"a\"")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a-b", "x": 0, "y": 0, "width": 1, "height": 1 },
                      { "name": "a_b", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a-b", "ticks": 1 } ] } ] }
        """, "one C# name")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "a b", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a b", "ticks": 1 } ] } ] }
        """, "no C# name")]
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png",
          "frames": [ { "name": "frames", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "frames", "ticks": 1 } ] } ] }
        """, "'Frames' class")]
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
        """, "has no frames")]
    // A field the format does not have, which is what a typo looks like.
    [InlineData("""
        { "formatVersion": 1, "texture": "p.png", "fps": 12,
          "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ],
          "clips": [ { "name": "c", "frames": [ { "frame": "a", "ticks": 1 } ] } ] }
        """, "malformed")]
    public void AMalformedDocumentIsRefusedWithTheDefectNamed(string json, string expected)
    {
        SpriteSheetFormatException failure =
            Assert.Throws<SpriteSheetFormatException>(() => SpriteSheetDocumentFile.Parse(json));

        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
