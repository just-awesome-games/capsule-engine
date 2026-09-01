using Capsule.Cli.Tiled;
using Capsule.Collision;
using Capsule.Rendering;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Documents;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TileLayerFormatTests
{
    [Fact]
    public void ATileTypesLayerAndCollidableFaces_SurviveTheirOwnRoundTrip()
    {
        string written = SceneDocumentFile.ToJson(Document("platform", CellFaces2D.Top));

        Assert.Contains("\"layer\": \"platform\"", written, StringComparison.Ordinal);
        Assert.Contains("\"top\"", written, StringComparison.Ordinal);

        TileDefinition read = Palette(SceneDocumentFile.Parse(written))[1];
        Assert.Equal("platform", read.Layer);
        Assert.Equal(CellFaces2D.Top, read.CollidableFaces);
        Assert.Equal(written, SceneDocumentFile.ToJson(SceneDocumentFile.Parse(written)));
    }

    [Fact]
    public void ATileTypeThatCollidesWithNothing_WritesNeitherLayerNorCollidableFaces()
    {
        string written = SceneDocumentFile.ToJson(Document(null, CellFaces2D.All));

        Assert.DoesNotContain("layer", written, StringComparison.Ordinal);
        Assert.DoesNotContain("collidableFaces", written, StringComparison.Ordinal);
        Assert.Null(Palette(SceneDocumentFile.Parse(written))[1].Layer);
    }

    // Every side is the default, so a tile that collides as its whole box says nothing about faces.
    [Fact]
    public void ATileTypeCollidingOnEverySide_WritesNoCollidableFaces()
    {
        string written = SceneDocumentFile.ToJson(Document("solid", CellFaces2D.All));

        Assert.DoesNotContain("collidableFaces", written, StringComparison.Ordinal);
        Assert.Equal(CellFaces2D.All, Palette(SceneDocumentFile.Parse(written))[1].CollidableFaces);
    }

    [Fact]
    public void AVersionOneDocument_IsRefused()
    {
        string written = SceneDocumentFile.ToJson(Document("solid", CellFaces2D.All))
            .Replace("\"formatVersion\": 2", "\"formatVersion\": 1", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("formatVersion 1 is unsupported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownFaceSpelling_FailsTheDocument()
    {
        string written = SceneDocumentFile.ToJson(Document("platform", CellFaces2D.Top))
            .Replace("\"top\"", "\"sideways\"", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("tileTypes[1].collidableFaces holds \"sideways\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("left, right, top, bottom", error.Message, StringComparison.Ordinal);
    }

    // Faces on a tile that collides as nothing describe sides of something that is never there.
    [Fact]
    public void CollidableFacesOnATileWithNoLayer_FailTheDocument()
    {
        string written = SceneDocumentFile.ToJson(Document("platform", CellFaces2D.Top))
            .Replace("\"layer\": \"platform\",\n", string.Empty, StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("collides as nothing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReservedEmptyEntry_MayNotCollide()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            2,
            1,
            [new TileDefinition("empty", null, "solid"), Ground("solid", CellFaces2D.All)],
            [0, 1]));

        Assert.Contains("no colour and no layer", error.Message, StringComparison.Ordinal);
    }

    // A tile on a layer with no face collides with nothing at all, so it is a mistake rather than a
    // second way to spell decoration.
    [Fact]
    public void ATileOnALayerWithNoCollidableFaces_IsRefusedByTheGrid()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            2,
            1,
            [TileGrid.EmptyTile, Ground("solid", CellFaces2D.None)],
            [0, 1]));

        Assert.Contains("no collidableFaces", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATileWithCollidableFacesButNoLayer_IsRefusedByTheGrid()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new TileGrid(
            16,
            2,
            1,
            [TileGrid.EmptyTile, Ground(null, CellFaces2D.Top)],
            [0, 1]));

        Assert.Contains("collidableFaces but no layer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_ReadsATilesLayerProperty()
    {
        SceneDocument document = ImportWithTileProperty(
            "{\"name\":\"layer\",\"type\":\"string\",\"value\":\" solid \"},");

        Assert.Equal("solid", Palette(document)[1].Layer);
        Assert.Equal(CellFaces2D.All, Palette(document)[1].CollidableFaces);
        Assert.Null(Palette(document)[2].Layer);
    }

    [Fact]
    public void Import_ReadsATilesCollidableFacesProperty()
    {
        SceneDocument document = ImportWithTileProperty(
            "{\"name\":\"layer\",\"type\":\"string\",\"value\":\"platform\"},{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\" top , \"},");

        Assert.Equal(CellFaces2D.Top, Palette(document)[1].CollidableFaces);
    }

    [Fact]
    public void Import_LeavesATileWithNoLayerPropertyCollidingWithNothing()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj");

        Assert.All(Palette(document).ToArray(), definition => Assert.Null(definition.Layer));
    }

    [Fact]
    public void Import_RejectsATileStillCarryingACollisionProperty()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"collision\",\"type\":\"string\",\"value\":\"box\"},"));

        Assert.Contains("no longer reads", error.Message, StringComparison.Ordinal);
        Assert.Contains("'layer'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'collidableFaces'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsALayerPropertyNamingMoreThanOneLayer()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"layer\",\"type\":\"string\",\"value\":\"solid,platform\"},"));

        Assert.Contains("naming 2 layers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsACollidableFacesPropertyThatSpellsSomethingElse()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                "{\"name\":\"layer\",\"type\":\"string\",\"value\":\"platform\"},{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\"sideways\"},"));

        Assert.Contains("naming 'sideways'", error.Message, StringComparison.Ordinal);
        Assert.Contains("left, right, top, bottom", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsCollidableFacesOnATileWithNoLayer()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\"top\"},"));

        Assert.Contains("no 'layer'", error.Message, StringComparison.Ordinal);
    }

    // A property that is there and names nothing is an authoring mistake, not a default: read as
    // absent, an empty collidableFaces would silently ship a solid tile.
    [Theory]
    [InlineData("")]
    [InlineData(" , , ")]
    public void Import_RejectsACollidableFacesPropertyThatNamesNothing(string authored)
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                $"{{\"name\":\"layer\",\"type\":\"string\",\"value\":\"platform\"}},{{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\"{authored}\"}},"));

        Assert.Contains("naming nothing", error.Message, StringComparison.Ordinal);
        Assert.Contains("remove the property", error.Message, StringComparison.Ordinal);
    }

    // And with no layer either, the empty property must still reach the faces-without-layer refusal
    // rather than passing as an absent one.
    [Theory]
    [InlineData("")]
    [InlineData(" , , ")]
    public void AnEmptyCollidableFacesPropertyWithNoLayer_StillReachesTheFacesWithoutLayerRefusal(string authored)
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                $"{{\"name\":\"collidableFaces\",\"type\":\"string\",\"value\":\"{authored}\"}},"));

        Assert.Contains("no 'layer'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" , ")]
    public void Import_RejectsALayerPropertyThatNamesNothing(string authored)
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty(
                $"{{\"name\":\"layer\",\"type\":\"string\",\"value\":\"{authored}\"}},"));

        Assert.Contains("naming nothing", error.Message, StringComparison.Ordinal);
    }

    // The retired field is refused on presence, whatever it holds: read as a string member, an
    // explicit null would look absent and a number or an object would fail as a JSON shape error,
    // and neither tells an author what took the field's place.
    [Theory]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("{ \"shape\": \"box\" }")]
    [InlineData("[\"box\"]")]
    [InlineData("\"box\"")]
    public void ACollisionFieldOfAnyValue_IsRefusedWithThePointerToItsReplacements(string value)
    {
        string written = SceneDocumentFile.ToJson(Document("solid", CellFaces2D.All))
            .Replace("\"layer\": \"solid\"", $"\"collision\": {value}", StringComparison.Ordinal);

        SceneDocumentFormatException error = Assert.Throws<SceneDocumentFormatException>(
            () => SceneDocumentFile.Parse(written));

        Assert.Contains("tileTypes[1] declares collision", error.Message, StringComparison.Ordinal);
        Assert.Contains("collidableFaces", error.Message, StringComparison.Ordinal);
    }

    // Several of Tiled's property types carry a string value, and a well-formed one of the wrong
    // type would otherwise import as a real layer name.
    [Fact]
    public void Import_RejectsALayerPropertyNotDeclaredAsAString()
    {
        TiledImportException error = Assert.Throws<TiledImportException>(
            () => ImportWithTileProperty("{\"name\":\"layer\",\"type\":\"file\",\"value\":\"solid\"},"));

        Assert.Contains("tileset 'terrain' tile 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("Class 'ground'", error.Message, StringComparison.Ordinal);
        Assert.Contains("as a 'file' property", error.Message, StringComparison.Ordinal);
    }

    private static SceneDocument ImportWithTileProperty(string property)
    {
        // Injected into the first tile's property list, ahead of its colour.
        string tileset = SceneDocumentFixtures.Read("tiles.tsj").Replace(
            "\"properties\":[\n                {\n                 \"name\":\"color\",\n                 \"type\":\"color\",\n                 \"value\":\"#ff4a5568\"\n                }]",
            $"\"properties\":[{property}\n                {{\n                 \"name\":\"color\",\n                 \"type\":\"color\",\n                 \"value\":\"#ff4a5568\"\n                }}]",
            StringComparison.Ordinal);

        Assert.NotEqual(SceneDocumentFixtures.Read("tiles.tsj"), tileset);

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);

        return TiledImporter.Import(workspace.Write("room.tmj", SceneDocumentFixtures.Read("room.tmj")));
    }

    private static ReadOnlySpan<TileDefinition> Palette(SceneDocument document) =>
        document.Entries[0].TileMap!.Value.Grid.TileTypes;

    private static TileDefinition Ground(string? layer, CellFaces2D collidableFaces) =>
        new("ground", new ColorRgba(0x4A, 0x55, 0x68), layer, collidableFaces);

    private static SceneDocument Document(string? layer, CellFaces2D collidableFaces) =>
        new(
            [
                new TileMapPlacement(
                    1,
                    new TileGrid(16, 2, 1, [TileGrid.EmptyTile, Ground(layer, collidableFaces)], [0, 1])),
            ],
            2);
}
