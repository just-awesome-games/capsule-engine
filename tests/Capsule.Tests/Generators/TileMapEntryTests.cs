using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using Capsule.Assets;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tiles;
using Microsoft.CodeAnalysis;

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

    private static readonly Lazy<SceneRegistry> Registry = new(static () =>
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            Game, logic: true, ("textures/frost.png", null), ("scenes/rink.scene.json", Document(string.Empty)));
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        return (SceneRegistry)GeneratorHarness.Loaded(compiled).GetType("Capsule.Generated.CapsuleScenes")!.GetProperty("Registry")!.GetValue(null)!;
    });

    [Fact]
    public void ATileMapEntry_ComposesItsGridAndPalette_AndTheSceneSpansIt()
    {
        Scene scene = Composed(Document(""", "type": "ice", "grip": 0.5, "frost": "textures/frost.png" """));
        TileMap map = Assert.IsType<TileMap>(Assert.Single(scene.Entities.ToArray()));
        TileType tile = map.TileAt(0, 0);

        Assert.Equal(("Game.Ice", (object?)0.5f), (tile.GetType().FullName, tile.GetType().GetProperty("Grip")!.GetValue(tile)));
        Assert.Equal(("slick", "solid"), (tile.Name, tile.Layer));
        Assert.Equal(TileTransform.FlipX, map.TransformAt(0, 0));
        Assert.True(map.TryGet<TileMapCollider2D>(out _));
        Assert.Equal(new Vector2(32f, 16f), scene.Size);
        Assert.Equal([new TextureHandle("textures/frost", ".png")], scene.CollectAssetPreloads().Textures);
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

        SceneDocumentFormatException refused = Assert.Throws<SceneDocumentFormatException>(() => Composed(document));

        Assert.Contains("entities[0] ('tile-map'", refused.Message, StringComparison.Ordinal);
        Assert.Contains(defect, refused.Message, StringComparison.Ordinal);
    }

    private static Scene Composed(string document) =>
        Registry.Value.Create(new SceneKey("scenes/rink"), SceneDocument.Parse(document));

    // One colliding tile-map entry whose palette entry "slick" carries what the test authors, painted mirrored at (0, 0).
    private static string Document(string authored) => $$$"""
        {"entities": [
          {"type": "tile-map", "tileSize": 16, "width": 2, "height": 1,
            "tileTypes": [{"name": "empty"}, {"name": "slick", "layer": "solid"{{{authored}}}}],
            "tiles": [1, 0], "transforms": [1, 2], "collider": true}
        ]}
        """;
}
