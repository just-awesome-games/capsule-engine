using Capsule.Maps;
using Capsule.Maps.Cli.Tiled;
using Capsule.Rendering;

namespace Capsule.Tests.Maps;

[Collection(MapWorkspaceCollection.Name)]
public sealed class TiledImportTests
{
    [Fact]
    public void Import_ReproducesTheCommittedMapByteForByte()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("room");

        Map map = TiledImporter.Import("room.tmj");

        Assert.Equal(MapFixtures.Read("room.map.json"), MapFile.ToJson(map));
    }

    [Fact]
    public void Import_PutsUnpaintedClassesInThePaletteInTileIdOrder()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("room");

        Map map = TiledImporter.Import("room.tmj");

        string[] types = [.. map.Grid.TileTypes.ToArray().Select(static definition => definition.Type)];

        Assert.Equal(["empty", "ground", "wall", "ledge", "hazard"], types);
    }

    [Theory]
    [InlineData("#cc718096", 0x71, 0x80, 0x96, 0xCC)]
    [InlineData("#CC718096", 0x71, 0x80, 0x96, 0xCC)]
    [InlineData("#718096", 0x71, 0x80, 0x96, 0xFF)]
    public void Import_ReordersTiledsAlphaFirstColourIntoRgba(string authored, int r, int g, int b, int a)
    {
        string tileset = Mutate(MapFixtures.Read("tiles.tsj"), "\"#cc718096\"", $"\"{authored}\"");

        using MapFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);
        string mapPath = workspace.Write("room.tmj", MapFixtures.Read("room.tmj"));

        Map map = TiledImporter.Import(mapPath);

        Assert.Equal(new ColorRgba((byte)r, (byte)g, (byte)b, (byte)a), map.Grid.TileTypes[3].Color);
    }

    [Fact]
    public void Import_AcceptsATileClassWithNoColourProperty()
    {
        string tileset = Mutate(MapFixtures.Read("tiles.tsj"), "\"name\":\"color\"", "\"name\":\"colour\"");
        using MapFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", tileset);

        Map map = TiledImporter.Import(workspace.Write("room.tmj", MapFixtures.Read("room.tmj")));

        Assert.Null(map.Grid.TileTypes[1].Color);
        Assert.Equal("ground", map.Grid.TileTypes[1].Type);
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
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("room");

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("room.tmj", tileSize: 8));

        Assert.Contains("has 16px tiles", error.Message, StringComparison.Ordinal);
        Assert.Contains("declares 8px", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_WithNoDeclaredTileSize_TakesTheMapsOwn()
    {
        string map = Mutate(MapFixtures.Read("room.tmj"), "\"tileheight\":16", "\"tileheight\":8");
        map = Mutate(map, "\"tilewidth\":16", "\"tilewidth\":8");

        using MapFixtures.Workspace workspace = new();
        workspace.Write("tiles.tsj", MapFixtures.Read("tiles.tsj"));

        Assert.Equal(8, TiledImporter.Import(workspace.Write("room.tmj", map)).Grid.TileSize);
    }

    [Fact]
    public void Import_ReadsTheClassAndTypeSpellingsAlike()
    {
        Map golden = MapFile.Load(MapFixtures.Path("room.map.json"));
        using MapFixtures.Workspace workspace = new();

        Map map = TiledImporter.Import(
            workspace.Write("room-tiled19.tmj", MapFixtures.Read("room-tiled19.tmj")));

        Assert.Equal(golden.Grid.TileSize, map.Grid.TileSize);
        Assert.Equal(golden.NextObjectId, map.NextObjectId);
        Assert.Equal(golden.Grid.TileTypes.ToArray(), map.Grid.TileTypes.ToArray());
        Assert.Equal(golden.Grid.Tiles.ToArray(), map.Grid.Tiles.ToArray());
        Assert.Equal(golden.Objects.ToArray(), map.Objects.ToArray());
    }

    [Fact]
    public void Import_ForwardSlashesTheSourcePathItStamps()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("maps/room");

        Map map = TiledImporter.Import(Path.Combine("maps", "room.tmj"));

        Assert.Equal("maps/room.tmj", map.Source?.Path);
    }

    [Fact]
    public void Import_SourceHashChangesWhenAnExternalTilesetChanges()
    {
        using MapFixtures.Workspace workspace = new();
        workspace.Write("room.tmj", MapFixtures.Read("room.tmj"));
        workspace.Write("tiles.tsj", MapFixtures.Read("tiles.tsj"));
        string first = TiledImporter.Import("room.tmj").Source!.Value.Hash;

        string changed = Mutate(MapFixtures.Read("tiles.tsj"), "#ff4a5568", "#ff4a5569");
        workspace.Write("tiles.tsj", changed);
        string second = TiledImporter.Import("room.tmj").Source!.Value.Hash;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Import_AcceptsAnExternalTilesetWithinTheTrackedRoot()
    {
        using MapFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/maps");
        workspace.Write("assets/tiles.tsj", MapFixtures.Read("tiles.tsj"));
        string map = Mutate(MapFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../tiles.tsj\"");
        workspace.Write("assets/maps/room.tmj", map);

        Map imported = TiledImporter.Import("assets/maps/room.tmj", dependencyRoot: "assets");

        Assert.Equal("ground", imported.Grid.TileTypes[1].Type);
    }

    [Fact]
    public void Import_RejectsAnExternalTilesetOutsideTheTrackedRoot()
    {
        using MapFixtures.Workspace workspace = new();
        Directory.CreateDirectory("assets/maps");
        workspace.Write("tiles.tsj", MapFixtures.Read("tiles.tsj"));
        string map = Mutate(MapFixtures.Read("room.tmj"), "\"source\":\"tiles.tsj\"", "\"source\":\"../../tiles.tsj\"");
        workspace.Write("assets/maps/room.tmj", map);

        TiledImportException error = Assert.Throws<TiledImportException>(
            () => TiledImporter.Import("assets/maps/room.tmj", dependencyRoot: "assets"));

        Assert.Contains("outside the tracked asset source root", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RefusesAnAbsoluteSourcePath()
    {
        using MapFixtures.Workspace workspace = MapFixtures.CopyMaps("room");

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
        string map = Mutate(MapFixtures.Read("room.tmj"), "\"width\":4", "\"width\":65536");
        map = Mutate(map, "\"height\":3", "\"height\":65536");

        TiledImportException error = Import(map, MapFixtures.Read("tiles.tsj"));

        Assert.Contains("65536x65536", error.Message, StringComparison.Ordinal);
    }

    private static TiledImportException ImportMutated(string from, string to, bool mutateTileset)
    {
        string map = MapFixtures.Read("room.tmj");
        string tileset = MapFixtures.Read("tiles.tsj");

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
        using MapFixtures.Workspace workspace = new();
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
