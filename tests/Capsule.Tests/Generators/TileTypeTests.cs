using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Capsule.Assets;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Tiles;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// A palette entry's type names a TileType subclass, which the scene composes with the engine's fields copied and
// the entry's properties set. The build checks each palette entry against its class as it checks an entity entry.
public sealed class TileTypeTests
{
    private const string Rink = "scenes/rink.scene.json";

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

    private static readonly (string Path, string? Content) Frost = ("textures/frost.png", null);

    [Fact]
    public void APaletteEntryNamingASubclass_ComposesAsThatClass_WithItsAuthoredValues()
    {
        string document = Document(""" "type": "ice", "properties": {"grip": 0.5, "frost": "textures/frost.png"} """);
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(Game, logic: true, (Rink, document), Frost);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Assembly game = GeneratorHarness.Loaded(compiled);
        SceneRegistry registry = (SceneRegistry)game.GetType("Capsule.Generated.CapsuleScenes")!.GetProperty("Registry")!.GetValue(null)!;
        Scene scene = registry.Create(new SceneKey("scenes/rink"), SceneDocumentFile.Parse(document));
        TileMap map = Assert.IsType<TileMap>(Assert.Single(scene.Entities.ToArray()));
        TileType tile = map.TileAt(0, 0);

        Assert.Equal(("Game.Ice", (object?)0.5f), (tile.GetType().FullName, tile.GetType().GetProperty("Grip")!.GetValue(tile)));
        Assert.Equal(("slick", "solid"), (tile.Name, tile.Layer));
        Assert.Equal([new TextureHandle("textures/frost", ".png")], scene.CollectAssetPreloads().Textures);
    }

    // Reported at the tile-map entry's opening brace, on the document's second line.
    [Theory]
    [InlineData("\"type\": \"lava\"", "CAP034", "tile-map entry 1's tile 'slick' has type 'lava', which no tile type claims. Declare the TileType subclass")]
    [InlineData("\"type\": \"ice\", \"properties\": {\"speed\": 1}", "CAP036", "tile-map entry 1's tile 'slick' sets 'speed', which 'Game.Ice' does not declare. Its authorable members are: grip, frost")]
    [InlineData("\"properties\": {\"grip\": 1}", "CAP036", "sets 'grip', which 'Capsule.Tiles.TileType' does not declare. Its authorable members are: none")]
    public void APaletteEntryItsClassRefuses_FailsTheBuildAtItsTileMapEntry(string authored, string id, string fix)
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = GeneratorHarness.CompileAgainstSources(Game, logic: true, (Rink, Document(authored)), Frost);

        Diagnostic refused = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal(id, refused.Id);
        Assert.Contains(fix, refused.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        FileLinePositionSpan at = refused.Location.GetLineSpan();
        Assert.Equal((Rink, 1, 2), (at.Path, at.StartLinePosition.Line, at.StartLinePosition.Character));
    }

    // One tile-map entry whose palette entry "slick" carries what the test authors, painted at (0, 0).
    private static string Document(string authored) => $$$"""
        {"formatVersion": 8, "entities": [
          {"id": 1, "type": "tile-map", "x": 0, "y": 0, "properties": {"tileSize": 16, "width": 2, "height": 1,
            "tileTypes": [{"name": "empty"}, {"name": "slick", "layer": "solid", {{{authored}}}}], "tiles": [1, 0]}}
        ], "nextEntityId": 2}
        """;
}
