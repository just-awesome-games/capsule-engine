using System.Numerics;
using System.Reflection;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Generators;

// A tile-map entry is the engine's TileMap placed as any entity is: its grid is its authorable members, and each
// palette entry is an object composing a TileType or the subclass its type names. The load checks it all.
public sealed class TileMapEntryTests
{
    private const string Game = """
        #nullable enable
        using Capsule.Assets;
        using Capsule.Scenes;
        using Capsule.Tiles;

        namespace Game;

        public sealed class Ice : TileType
        {
            [Authorable]
            public float Grip { get; init; } = 1f;

            [Authorable]
            public TextureHandle Frost { get; init; }
        }
        """;

    private static readonly Lazy<SceneRegistry> Registry = new(static () => GeneratorHarness.Registry(
        GeneratorHarness.CompileAgainstSources(Game, logic: true, ("textures/frost.png", null), ("scenes/rink.scene.json", Document(string.Empty)))));

    [Fact]
    public void ATileMapEntry_ComposesItsGridAndPalette_AndTheSceneSpansIt()
    {
        Scene scene = Composed(Document(""", "type": "ice", "grip": 0.5, "frost": "textures/frost.png" """));
        TileMap map = Assert.IsType<TileMap>(Assert.Single(scene.Entities.ToArray()));
        TileType tile = map.TileAt(0, 0);

        Assert.Equal(("Game.Ice", (object?)0.5f), (tile.GetType().FullName, tile.GetType().GetProperty("Grip")!.GetValue(tile)));
        Assert.Equal(("slick", "solid"), (tile.Name, tile.Layer));
        Assert.Equal(TileTransform.FlipX, map.TransformAt(0, 0));
        Assert.True(map.Has<TileMapCollider2D>());
        Assert.Equal(new Vector2(32f, 16f), scene.Size);
        Assert.Equal([new TextureHandle("textures/frost", ".png")], scene.CollectAssetPreloads(entryPayload: null).Textures);
    }

    // Every map composed from one document shares its cells until it edits one. The edit stays in its own scene,
    // and a scene composed from the document afterwards draws and collides as authored.
    [Fact]
    public void AnEditToOneComposedMap_LeavesTheNextCompositionAsAuthored()
    {
        SceneDocument document = SceneDocument.Parse(Document(string.Empty));
        Scene first = Registry.Value.Create(new SceneKey("scenes/rink"), document);
        using SimulationHost running = new(first);
        TileMap edited = Assert.IsType<TileMap>(Assert.Single(first.Entities.ToArray()));
        edited.RemoveTile(0, 0);

        Scene second = Registry.Value.Create(new SceneKey("scenes/rink"), document);
        using SimulationHost restarted = new(second);
        TileMap authored = Assert.IsType<TileMap>(Assert.Single(second.Entities.ToArray()));

        Assert.Equal(TileGrid.EmptyTileName, edited.TileAt(0, 0).Name);
        Assert.Equal(("slick", TileTransform.FlipX), (authored.TileAt(0, 0).Name, authored.TransformAt(0, 0)));
        Assert.False(first.Collision.Raycast(new Vector2(8f, -8f), Vector2.UnitY, 32f, CollisionFilter.Everything, out _));
        Assert.True(second.Collision.Raycast(new Vector2(8f, -8f), Vector2.UnitY, 32f, CollisionFilter.Everything, out _));
    }

    // The palette is read and the grid built once per document. A later scene holds the first one's, and still
    // preloads the texture a tile type names. A built document's compiled cells compose the same way.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySceneComposedFromOneDocument_HoldsTheSameGridAndTileTypes_AndPreloadsWhatTheyName(bool compiled)
    {
        SceneDocument document = SceneDocument.Parse(Document(""", "type": "ice", "frost": "textures/frost.png" """));
        document = compiled ? CompiledTiles.Compile(document, [TileMap.Key]) : document;
        Scene first = Registry.Value.Create(new SceneKey("scenes/rink"), document);
        Scene second = Registry.Value.Create(new SceneKey("scenes/rink"), document);
        TileMap map = Assert.IsType<TileMap>(Assert.Single(first.Entities.ToArray()));
        TileMap again = Assert.IsType<TileMap>(Assert.Single(second.Entities.ToArray()));

        Assert.Same(map.Grid, again.Grid);
        Assert.Same(map.TileAt(0, 0), again.TileAt(0, 0));
        Assert.Equal([new TextureHandle("textures/frost", ".png")], second.CollectAssetPreloads(entryPayload: null).Textures);
    }

    // Maps of one document that author the same palette hold one read of it, and a map authoring another holds
    // its own. Maps authoring the same tiles in another shape each build their own cells.
    [Fact]
    public void MapsOfOneDocumentAuthoringTheSamePalette_HoldTheSameTileTypes()
    {
        const string Palette = """[{"name": "empty"}, {"name": "slick", "layer": "solid"}]""";
        const string Other = """[{"name": "empty"}, {"name": "slick"}]""";
        static string Map(string palette, int width, int height) =>
            $$"""{"type": "tile-map", "tileSize": 16, "width": {{width}}, "height": {{height}}, "tileTypes": {{palette}}, "tiles": [1, 0]}""";

        Scene scene = Composed($$"""{"entities": [{{Map(Palette, 2, 1)}}, {{Map(Palette, 1, 2)}}, {{Map(Other, 2, 1)}}]}""");
        TileMap[] maps = [.. scene.Entities.ToArray().Cast<TileMap>()];

        Assert.Same(maps[0].TileAt(0, 0), maps[1].TileAt(0, 0));
        Assert.Same(maps[0].TileAt(1, 0), maps[1].TileAt(0, 1));
        Assert.NotSame(maps[0].TileAt(0, 0), maps[2].TileAt(0, 0));
        Assert.Equal(("solid", null), (maps[0].TileAt(0, 0).Layer, maps[2].TileAt(0, 0).Layer));
        Assert.Equal((new Vector2(32f, 16f), new Vector2(16f, 32f)), (maps[0].Size, maps[1].Size));
    }

    // A palette the first composition refuses is kept by nothing, and the document fails the same way again.
    [Fact]
    public void ADocumentWhosePaletteIsRefused_IsRefusedByEveryComposition()
    {
        SceneDocument document = SceneDocument.Parse(Document(""", "grip": 1 """));

        for (int composition = 0; composition < 2; composition++)
        {
            SceneDocumentFormatException refused = Assert.Throws<SceneDocumentFormatException>(
                () => Registry.Value.Create(new SceneKey("scenes/rink"), document));
            Assert.Contains("sets 'tileTypes[1].grip', which no authorable member takes", refused.Message, StringComparison.Ordinal);
        }
    }

    // Every defect fails the load naming the entry, whichever of the reader, the palette or the grid finds it.
    [Theory]
    [InlineData(""", "type": "lava" """, "sets 'tileTypes[1].type' to \"lava\"")]
    [InlineData(""", "grip": 1 """, "sets 'tileTypes[1].grip', which no authorable member takes")]
    [InlineData("", "tiles has 1 entries but width 2 x height 1 requires 2", "[1, 0]", "[1]")]
    [InlineData("", "tiles[1] is 7", "[1, 0]", "[1, 7]")]
    [InlineData("", "transforms[1] is 8", "[1, 2]", "[1, 8]")]
    [InlineData("", "anchored at the world origin", "\"tileSize\"", "\"x\": 8, \"tileSize\"")]
    public void AnInvalidTileMapEntry_FailsTheLoadNamingIt(string authored, string defect, string? find = null, string? replace = null)
    {
        string document = Document(authored);
        document = find is null ? document : document.Replace(find, replace, StringComparison.Ordinal);

        GeneratorHarness.AssertLoadRefused(() => Composed(document), "scene document 'scenes/rink': entities[0] ('tile-map'", defect);
    }

    private static Scene Composed(string document) => GeneratorHarness.Composed(Registry.Value, document, "scenes/rink");

    // One colliding tile-map entry whose palette entry "slick" carries what the test authors, painted mirrored at (0, 0).
    private static string Document(string authored) => $$$"""
        {"entities": [
          {"type": "tile-map", "tileSize": 16, "width": 2, "height": 1,
            "tileTypes": [{"name": "empty"}, {"name": "slick", "layer": "solid"{{{authored}}}}],
            "tiles": [1, 0], "transforms": [1, 2], "collider": true}
        ]}
        """;
}
