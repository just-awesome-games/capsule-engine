using Capsule.Cli.Tiled;
using Capsule.Rendering;
using Capsule.Scenes.Documents;

namespace Capsule.Tests.Documents;

[Collection(SceneWorkspaceCollection.Name)]
public sealed class TiledImportTests
{
    [Fact]
    public void Import_ReproducesTheCommittedDocumentByteForByte()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj");

        Assert.Equal(SceneDocumentFixtures.Read("room.scene.json"), SceneDocumentFile.ToJson(document));
    }

    [Fact]
    public void Import_PutsUnpaintedClassesInThePaletteInTileIdOrder()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        SceneDocument document = TiledImporter.Import("room.tmj");

        string[] types = [.. document.Grid!.TileTypes.ToArray().Select(static definition => definition.Type)];

        Assert.Equal(["empty", "ground", "wall", "ledge", "hazard"], types);
    }

    [Theory]
    [InlineData("#cc718096", 0x71, 0x80, 0x96, 0xCC)]
    [InlineData("#CC718096", 0x71, 0x80, 0x96, 0xCC)]
    [InlineData("#718096", 0x71, 0x80, 0x96, 0xFF)]
    public void Import_ReordersTiledsAlphaFirstColourIntoRgba(string authored, int r, int g, int b, int a)
    {
        string tileset = Mutate(SceneDocumentFixtures.Read("tiles.tsj"), "\"#cc718096\"", $"\"{authored}\"");

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);
        string mapPath = workspace.Write("room.tmj", SceneDocumentFixtures.Read("room.tmj"));

        SceneDocument document = TiledImporter.Import(mapPath);

        Assert.Equal(new ColorRgba((byte)r, (byte)g, (byte)b, (byte)a), document.Grid!.TileTypes[3].Color);
    }

    [Fact]
    public void Import_AcceptsATileClassWithNoColourProperty()
    {
        string tileset = Mutate(SceneDocumentFixtures.Read("tiles.tsj"), "\"name\":\"color\"", "\"name\":\"colour\"");
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);

        SceneDocument document = TiledImporter.Import(workspace.Write("room.tmj", SceneDocumentFixtures.Read("room.tmj")));

        Assert.Null(document.Grid!.TileTypes[1].Color);
        Assert.Equal("ground", document.Grid!.TileTypes[1].Type);
    }

    [Fact]
    public void Import_RejectsAColourPropertyTiledCouldNotHaveWritten()
    {
        TiledImportException error = ImportMutated("\"#ff2d3748\"", "\"slate\"", mutateTileset: true);

        Assert.Contains("not a Tiled colour", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"type\":\"string\",")]
    [InlineData("")]
    public void Import_RejectsAColourPropertyNotDeclaredAsAColour(string declaredType)
    {
        TiledImportException error = ImportMutated("\"type\":\"color\",", declaredType, mutateTileset: true);

        Assert.Contains("tileset 'terrain' tile 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("Class 'ground'", error.Message, StringComparison.Ordinal);
        Assert.Contains("as a 'string' property", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsAMapWhoseTileSizeIsNotTheDeclaredOne()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("room.tmj", tileSize: 8));

        Assert.Contains("has 16px tiles", error.Message, StringComparison.Ordinal);
        Assert.Contains("declares 8px", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_WithNoDeclaredTileSize_TakesTheMapsOwn()
    {
        string map = Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"tileheight\":16", "\"tileheight\":8");
        map = Mutate(map, "\"tilewidth\":16", "\"tilewidth\":8");

        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));

        Assert.Equal(8, TiledImporter.Import(workspace.Write("room.tmj", map)).Grid!.TileSize);
    }

    [Fact]
    public void Import_ReadsTheClassAndTypeSpellingsAlike()
    {
        SceneDocument golden = SceneDocumentFile.Load(SceneDocumentFixtures.Path("room.scene.json"));
        using SceneDocumentFixtures.Workspace workspace = new();

        SceneDocument document = TiledImporter.Import(
            workspace.Write("room-tiled19.tmj", SceneDocumentFixtures.Read("room-tiled19.tmj")));

        Assert.Equal(golden.Grid!.TileSize, document.Grid!.TileSize);
        Assert.Equal(golden.NextEntityId, document.NextEntityId);
        Assert.Equal(golden.Grid.TileTypes.ToArray(), document.Grid.TileTypes.ToArray());
        Assert.Equal(golden.Grid.Tiles.ToArray(), document.Grid.Tiles.ToArray());
        Assert.Equal(golden.Entities.ToArray(), document.Entities.ToArray());
    }

    [Fact]
    public void Import_ForwardSlashesTheSourcePathItStamps()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("scenes/room");

        SceneDocument document = TiledImporter.Import(Path.Combine("scenes", "room.tmj"));

        Assert.Equal("scenes/room.tmj", document.Source?.Path);
    }

    [Fact]
    public void Import_SourceHashChangesWhenAnExternalTilesetChanges()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("room.tmj", SceneDocumentFixtures.Read("room.tmj"));
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        string first = TiledImporter.Import("room.tmj").Source!.Value.Hash;

        string changed = Mutate(SceneDocumentFixtures.Read("tiles.tsj"), "#ff4a5568", "#ff4a5569");
        workspace.Write("tiles.tsj", changed);
        string second = TiledImporter.Import("room.tmj").Source!.Value.Hash;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Import_AcceptsAnExternalTilesetWithinTheTrackedRoot()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/scenes");
        workspace.Write("assets/tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        string map = Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../tiles.tsj\"");
        workspace.Write("assets/scenes/room.tmj", map);

        SceneDocument imported = TiledImporter.Import("assets/scenes/room.tmj", dependencyRoot: "assets");

        Assert.Equal("ground", imported.Grid!.TileTypes[1].Type);
    }

    [Fact]
    public void Import_RejectsAnExternalTilesetOutsideTheTrackedRoot()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/scenes");
        workspace.Write("tiles.tsj", SceneDocumentFixtures.Read("tiles.tsj"));
        string map = Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../../tiles.tsj\"");
        workspace.Write("assets/scenes/room.tmj", map);

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("assets/scenes/room.tmj", dependencyRoot: "assets"));

        Assert.Contains("outside the tracked asset source root", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RefusesAnAbsoluteSourcePath()
    {
        using SceneDocumentFixtures.Workspace workspace = SceneDocumentFixtures.CopyTiledSources("room");

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import(Path.GetFullPath("room.tmj")));

        Assert.Contains("must be relative", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"orientation\":\"orthogonal\"", "\"orientation\":\"isometric\"", "orthogonal maps only")]
    [InlineData("\"infinite\":false", "\"infinite\":true", "infinite map")]
    [InlineData("\"tileheight\":16", "\"tileheight\":8", "square tiles only")]
    [InlineData("\"type\":\"tilelayer\"", "\"type\":\"imagelayer\"", "unsupported layer type")]
    [InlineData("\"type\":\"objectgroup\"", "\"type\":\"tilelayer\"", "more than one tile layer")]
    [InlineData("\"type\":\"tilelayer\"", "\"type\":\"objectgroup\"", "no tile layer")]
    [InlineData("1, 1, 1, 4]", "1, 1, 1, 2147483649]", "flipped or rotated")]
    [InlineData("1, 1, 1, 4]", "1, 1, 1, 5]", "has no Class")]
    [InlineData("1, 1, 1, 4]", "1, 1, 1, 4, 0]", "requires 12")]
    [InlineData("\"type\":\"coin\"", "\"type\":\"\"", "typed by its Class")]
    [InlineData("\"source\":\"tiles.tsj\"", "\"source\":\"tiles.tsx\"", "XML")]
    [InlineData("\"source\":\"tiles.tsj\"", "\"source\":\"missing.tsj\"", "is missing")]
    [InlineData("\"data\":[0, 0, 0, 0, 1, 1, 2, 0, 1, 1, 1, 4],", "\"data\":\"AAAA\",\"encoding\":\"base64\",", "CSV")]
    public void Import_RejectsWhatItCannotRepresent(string from, string to, string expected)
    {
        TiledImportException error = ImportMutated(from, to, mutateTileset: false);

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsAClassDefinedByTwoTiles()
    {
        TiledImportException error = ImportMutated("\"type\":\"ledge\"", "\"type\":\"wall\"", mutateTileset: true);

        Assert.Contains("more than one tile", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsAClassThatShadowsTheEmptyTileType()
    {
        TiledImportException error = ImportMutated("\"type\":\"ledge\"", "\"type\":\"empty\"", mutateTileset: true);

        Assert.Contains("reserved", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectsAGridWhoseAreaOverflowsAnInt()
    {
        string map = Mutate(SceneDocumentFixtures.Read("room.tmj"), "\"width\":4", "\"width\":65536");
        map = Mutate(map, "\"height\":3", "\"height\":65536");

        TiledImportException error = Import(map, SceneDocumentFixtures.Read("tiles.tsj"));

        Assert.Contains("65536x65536", error.Message, StringComparison.Ordinal);
    }

    private static TiledImportException ImportMutated(string from, string to, bool mutateTileset)
    {
        string map = SceneDocumentFixtures.Read("room.tmj");
        string tileset = SceneDocumentFixtures.Read("tiles.tsj");

        if (mutateTileset)
        {
            tileset = Mutate(tileset, from, to);
        }
        else
        {
            map = Mutate(map, from, to);
        }

        return Import(map, tileset);
    }

    private static TiledImportException Import(string map, string tileset)
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);
        string mapPath = workspace.Write("room.tmj", map);

        return Assert.Throws<TiledImportException>(() => TiledImporter.Import(mapPath));
    }

    private static string Mutate(string text, string from, string to)
    {
        Assert.Contains(from, text, StringComparison.Ordinal);
        return text.Replace(from, to, StringComparison.Ordinal);
    }
}
