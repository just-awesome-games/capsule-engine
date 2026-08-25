using Capsule.Levels;

namespace Capsule.Tests.Levels;

/// <summary>
/// The format's contract: what a level file may say, and the exact bytes one is written back
/// as. The canonical form is load-bearing — the CLI's validate gate byte-compares against it.
/// </summary>
public sealed class LevelFileTests
{
    // Real-shaped: the format requires 64 lowercase hex characters, so a placeholder here
    // would exercise a level shape the constructor no longer admits.
    private const string Sha256 = "c304030d3d53c9c440cd5d251080080a16b34be3832ad1218b2b63cae622cf6d";

    [Fact]
    public void Parse_RejectsATilesArrayThatDoesNotFillTheGrid()
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => LevelFile.Parse(LevelText(tiles: "[0]")));

        Assert.Contains("requires 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsATileWithNoTileType()
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => LevelFile.Parse(LevelText(tiles: "[0, 7]")));

        Assert.Contains("tiles[1] is 7", error.Message, StringComparison.Ordinal);
    }

    // Index 0 is the format's one reserved slot: every unpainted cell points at it.
    [Fact]
    public void Parse_RejectsAPaletteThatDoesNotBeginWithEmpty()
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => LevelFile.Parse(LevelText(tileTypes: """["ground", "wall"]""", tiles: "[0, 1]")));

        Assert.Contains("tileTypes[0] must be \"empty\"", error.Message, StringComparison.Ordinal);
    }

    // A repeated name makes TileTypeAt ambiguous, and the importer's bijectivity rule assumes
    // this cannot happen in a file either.
    [Fact]
    public void Parse_RejectsARepeatedTileTypeName()
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => LevelFile.Parse(LevelText(tileTypes: """["empty", "ground", "ground"]""")));

        Assert.Contains("must be unique", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsDuplicateEntityIds()
    {
        string entities = """[{"id": 1, "type": "coin", "x": 0, "y": 0}, {"id": 1, "type": "coin", "x": 8, "y": 0}]""";

        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => LevelFile.Parse(LevelText(entities: entities, nextEntityId: 2)));

        Assert.Contains("appears more than once", error.Message, StringComparison.Ordinal);
    }

    // nextEntityId is the promise that ids are never reused; an id at or above it means the
    // counter was rewound, and the next assign-ids run would hand out a collision.
    [Fact]
    public void Parse_RejectsAnEntityIdThatNextEntityIdWouldHandOutAgain()
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => LevelFile.Parse(LevelText(entities: """[{"id": 4, "type": "coin", "x": 0, "y": 0}]""", nextEntityId: 4)));

        Assert.Contains("is not below nextEntityId 4", error.Message, StringComparison.Ordinal);
    }

    // The message is the whole point of this error: nothing in the file tells a hand-author
    // that a tool exists to fix it, so the message must.
    [Fact]
    public void Parse_NamesTheFixWhenAnEntityHasNoId()
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => LevelFile.Parse(LevelText(entities: """[{"type": "coin", "x": 128, "y": 64}]""")));

        Assert.Equal(
            "entity 'coin' at (128, 64) has no id — run: Capsule.Levels.Cli assign-ids <file>",
            error.Message);
    }

    // Strict by choice: a typo'd field in a hand-authored level would otherwise be silently
    // dropped and only surface as wrong behaviour in play.
    [Fact]
    public void Parse_RejectsAFieldTheFormatDoesNotDefine()
    {
        Assert.Throws<LevelFormatException>(() => LevelFile.Parse(LevelText(extra: ""","spawn": [0, 0]""")));
    }

    // Pins the canonical form whole: field order, indent, LF, the trailing newline, and that
    // an absent source block is omitted rather than written as null.
    [Fact]
    public void ToJson_WritesTheCanonicalForm()
    {
        Level level = new(16, 2, 1, ["empty", "ground"], [0, 1], [new LevelEntity(1, "coin", 8f, 0f)], 2);

        string expected = string.Join(
            '\n',
            "{",
            "  \"tileSize\": 16,",
            "  \"width\": 2,",
            "  \"height\": 1,",
            "  \"tileTypes\": [",
            "    \"empty\",",
            "    \"ground\"",
            "  ],",
            "  \"tiles\": [",
            "    0,",
            "    1",
            "  ],",
            "  \"entities\": [",
            "    {",
            "      \"id\": 1,",
            "      \"type\": \"coin\",",
            "      \"x\": 8,",
            "      \"y\": 0",
            "    }",
            "  ],",
            "  \"nextEntityId\": 2",
            "}",
            string.Empty);

        Assert.Equal(expected, LevelFile.ToJson(level));
    }

    // Width * Height as an int wraps: 65536 x 65536 is 0, which an empty tiles array would
    // have satisfied, leaving every TileAt on the level to throw instead.
    [Fact]
    public void Constructor_RejectsAGridWhoseAreaOverflowsAnInt()
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => new Level(16, 65536, 65536, ["empty"], [], [], 1));

        Assert.Contains("requires 4294967296", error.Message, StringComparison.Ordinal);
    }

    // Neither has a JSON number, so either would construct a level that cannot be written out.
    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    public void Constructor_RejectsANonFiniteEntityPosition(float x, float y)
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => new Level(16, 2, 1, ["empty", "ground"], [0, 1], [new LevelEntity(1, "coin", x, y)], 2));

        Assert.Contains("not a position", error.Message, StringComparison.Ordinal);
    }

    // A half-filled block writes a source object that Parse rejects: the level would fail its
    // own round trip.
    [Theory]
    [InlineData("", "room.tmj", Sha256)]
    [InlineData("tiled", "", Sha256)]
    [InlineData("tiled", "room.tmj", "")]
    public void Constructor_RejectsAnIncompleteSourceBlock(string tool, string path, string hash)
    {
        Assert.Throws<LevelFormatException>(
            () => new Level(16, 2, 1, ["empty", "ground"], [0, 1], [], 1, new LevelSource(tool, path, hash)));
    }

    [Fact]
    public void Constructor_RejectsADefaultSourceBlock()
    {
        Assert.Throws<LevelFormatException>(
            () => new Level(16, 2, 1, ["empty", "ground"], [0, 1], [], 1, default(LevelSource)));
    }

    // The source block only means anything if it resolves on someone else's machine: an
    // absolute or backslashed path is a level that can never pass the validate gate elsewhere.
    [Theory]
    [InlineData("..\\maps\\room.tmj")]
    [InlineData("maps\\room.tmj")]
    [InlineData("/maps/room.tmj")]
    [InlineData("C:/maps/room.tmj")]
    public void Constructor_RejectsASourcePathThatIsNotRelativeAndPortable(string path)
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => new Level(16, 2, 1, ["empty", "ground"], [0, 1], [], 1, new LevelSource("tiled", path, Sha256)));

        Assert.Contains("must be relative", error.Message, StringComparison.Ordinal);
    }

    // A hash no importer could have produced can only ever mismatch, so the gate would report
    // a stale level where the real defect is a hand-written source block.
    [Theory]
    [InlineData("abc123")]
    [InlineData("C304030D3D53C9C440CD5D251080080A16B34BE3832AD1218B2B63CAE622CF6D")]
    [InlineData("g304030d3d53c9c440cd5d251080080a16b34be3832ad1218b2b63cae622cf6d")]
    public void Constructor_RejectsAHashThatIsNotALowercaseSha256(string hash)
    {
        LevelFormatException error = Assert.Throws<LevelFormatException>(
            () => new Level(16, 2, 1, ["empty", "ground"], [0, 1], [], 1, new LevelSource("tiled", "room.tmj", hash)));

        Assert.Contains("64 lowercase hex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_RoundTripsALevelWithASourceBlock()
    {
        Level level = new(
            8,
            2,
            1,
            ["empty", "ground"],
            [1, 0],
            [new LevelEntity(3, "player", 40.5f, 24f)],
            4,
            new LevelSource("tiled", "../maps/room.tmj", Sha256));

        Level round = LevelFile.Parse(LevelFile.ToJson(level));

        Assert.Equal(LevelFile.ToJson(level), LevelFile.ToJson(round));
        Assert.Equal(level.Source, round.Source);
    }

    // Row-major is a contract a reader cannot check: a transposed grid still has the right
    // number of tiles and still validates.
    [Fact]
    public void TileTypeAt_ReadsTheGridRowMajor()
    {
        Level level = new(16, 2, 2, ["empty", "ground", "wall"], [0, 1, 2, 0], [], 1);

        Assert.Equal("ground", level.TileTypeAt(1, 0));
        Assert.Equal("wall", level.TileTypeAt(0, 1));
    }

    private static string LevelText(
        string tileTypes = """["empty", "ground"]""",
        string tiles = "[0, 1]",
        string entities = "[]",
        int nextEntityId = 1,
        string extra = "") =>
        $$"""
        {
          "tileSize": 16,
          "width": 2,
          "height": 1,
          "tileTypes": {{tileTypes}},
          "tiles": {{tiles}},
          "entities": {{entities}},
          "nextEntityId": {{nextEntityId}}{{extra}}
        }
        """;
}
